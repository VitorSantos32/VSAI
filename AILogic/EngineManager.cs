using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using VSAI.Class;
using Class;
using Visuality;
using LogLevel = Other.LogManager.LogLevel;
using LogManager = Other.LogManager;

namespace VSAI.AILogic
{
    public enum AIEngine
    {
        DirectML,
        CUDA,
        TensorRT,
        OpenVINO,
        CPU // Internal fallback
    }

    internal static class EngineManager
    {
        private static bool _initialized = false;

        // Para CUDA/TensorRT: o onnxruntime.dll GPU já está no build (Microsoft.ML.OnnxRuntime.Gpu.Windows).
        // Só precisamos baixar o onnxruntime_providers_cuda.dll quando o usuário selecionar CUDA.
        // Para OpenVINO: pacote separado da Intel.
        public static readonly Dictionary<AIEngine, string> EngineUrls = new()
        {
            { AIEngine.CUDA, "https://api.nuget.org/v3-flatcontainer/microsoft.ml.onnxruntime.gpu.windows/1.22.0/microsoft.ml.onnxruntime.gpu.windows.1.22.0.nupkg" },
            { AIEngine.TensorRT, "https://api.nuget.org/v3-flatcontainer/microsoft.ml.onnxruntime.gpu.windows/1.22.0/microsoft.ml.onnxruntime.gpu.windows.1.22.0.nupkg" },
            { AIEngine.OpenVINO, "https://api.nuget.org/v3-flatcontainer/intel.ml.onnxruntime.openvino/1.20.0/intel.ml.onnxruntime.openvino.1.20.0.nupkg" }
        };

        // Apenas os DLLs do provider CUDA/TensorRT que precisam ser baixados (o onnxruntime.dll já está no app)
        private static readonly string[] CudaProviderDlls = new[]
        {
            "onnxruntime_providers_cuda.dll",
            "onnxruntime_providers_shared.dll",
            "onnxruntime_providers_tensorrt.dll"
        };

        public static string GetEngineDirectory(AIEngine engine)
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            if (engine == AIEngine.DirectML || engine == AIEngine.CUDA || engine == AIEngine.TensorRT)
            {
                // Esses engines usam o onnxruntime.dll da pasta raiz do app (já incluído no build)
                return basePath;
            }
            return Path.Combine(basePath, "bin", "engines", engine.ToString());
        }

        /// <summary>
        /// CUDA/TensorRT: verifica se onnxruntime_providers_cuda.dll está na pasta do app.
        /// O onnxruntime.dll GPU já está sempre presente no build.
        /// OpenVINO: verifica se os arquivos foram baixados na pasta do engine.
        /// </summary>
        public static bool IsEngineDownloaded(AIEngine engine)
        {
            if (engine == AIEngine.DirectML) return true;
            if (engine == AIEngine.CPU) return true;

            if (engine == AIEngine.CUDA)
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                return File.Exists(Path.Combine(appDir, "onnxruntime_providers_cuda.dll"));
            }

            if (engine == AIEngine.TensorRT)
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                return File.Exists(Path.Combine(appDir, "onnxruntime_providers_cuda.dll")) &&
                       File.Exists(Path.Combine(appDir, "onnxruntime_providers_tensorrt.dll"));
            }

            // OpenVINO: verifica pasta específica do engine
            string engineDir = GetEngineDirectory(engine);
            return File.Exists(Path.Combine(engineDir, "onnxruntime.dll"));
        }

        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                var assembly = typeof(Microsoft.ML.OnnxRuntime.SessionOptions).Assembly;
                NativeLibrary.SetDllImportResolver(assembly, ResolveOnnxRuntimeLibrary);
                _initialized = true;
                LogManager.Log(LogLevel.Info, "ONNX Runtime Custom DLL Resolver inicializado com sucesso.");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Error, $"Falha ao inicializar DLL Resolver: {ex.Message}", true);
            }
        }

        private static IntPtr ResolveOnnxRuntimeLibrary(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
        {
            // Para CUDA/TensorRT/DirectML: o onnxruntime.dll GPU está na pasta raiz do app.
            // O .NET o encontra automaticamente — retornamos Zero para usar o comportamento padrão.
            //
            // Para OpenVINO: o onnxruntime.dll fica na pasta do engine específico.
            if (libraryName == "onnxruntime")
            {
                AIEngine selectedEngine = AimSettings.SelectedEngine;

                if (selectedEngine == AIEngine.OpenVINO)
                {
                    string engineDllPath = Path.Combine(GetEngineDirectory(selectedEngine), "onnxruntime.dll");
                    if (File.Exists(engineDllPath))
                    {
                        try
                        {
                            string engineDir = Path.GetDirectoryName(engineDllPath)!;

                            // Adiciona pasta do engine ao PATH para que as DLLs nativas sejam encontradas
                            try
                            {
                                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                                if (!currentPath.Split(Path.PathSeparator).Contains(engineDir, StringComparer.OrdinalIgnoreCase))
                                {
                                    Environment.SetEnvironmentVariable("PATH", engineDir + Path.PathSeparator + currentPath);
                                }
                            }
                            catch (Exception pathEx)
                            {
                                LogManager.Log(LogLevel.Warning, $"[Resolver] Falha ao adicionar pasta do engine ao PATH: {pathEx.Message}");
                            }

                            string sharedDll = Path.Combine(engineDir, "onnxruntime_providers_shared.dll");
                            if (File.Exists(sharedDll)) NativeLibrary.Load(sharedDll);

                            LogManager.Log(LogLevel.Info, $"[Resolver] Carregando onnxruntime.dll para OpenVINO de {engineDllPath}");
                            return NativeLibrary.Load(engineDllPath);
                        }
                        catch (Exception ex)
                        {
                            LogManager.Log(LogLevel.Error, $"[Resolver] Falha ao carregar DLLs OpenVINO de {engineDllPath}: {ex.Message}. Usando padrão.", true);
                        }
                    }
                    else
                    {
                        LogManager.Log(LogLevel.Warning, $"[Resolver] DLL do OpenVINO não encontrada em {engineDllPath}. Usando padrão.");
                    }
                }
            }
            return IntPtr.Zero; // Usa o caminho de busca padrão do .NET
        }

        public static bool IsOnnxRuntimeLoaded()
        {
            try
            {
                foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
                {
                    if (module.ModuleName != null && module.ModuleName.Equals("onnxruntime.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Warning, $"Falha ao inspecionar módulos do processo: {ex.Message}");
            }
            return false;
        }

        public static bool IsCudaInstalled()
        {
            try
            {
                // Verifica PATH do sistema por cudart64_12.dll
                var pathVar = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathVar))
                {
                    var paths = pathVar.Split(Path.PathSeparator);
                    foreach (var path in paths)
                    {
                        if (string.IsNullOrWhiteSpace(path)) continue;
                        try
                        {
                            if (Directory.Exists(path) && File.Exists(Path.Combine(path, "cudart64_12.dll")))
                            {
                                return true;
                            }
                        }
                        catch { }
                    }
                }

                // Verifica variável CUDA_PATH
                var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
                if (!string.IsNullOrEmpty(cudaPath) && Directory.Exists(cudaPath))
                {
                    if (File.Exists(Path.Combine(cudaPath, "bin", "cudart64_12.dll")))
                    {
                        return true;
                    }
                }

                // Verifica pasta padrão do CUDA Toolkit
                string defaultCudaBase = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "NVIDIA GPU Computing Toolkit", "CUDA");
                if (Directory.Exists(defaultCudaBase))
                {
                    foreach (var versionDir in Directory.GetDirectories(defaultCudaBase))
                    {
                        if (File.Exists(Path.Combine(versionDir, "bin", "cudart64_12.dll")))
                            return true;
                        // Também verifica cudart64_12x.dll (variações de versão)
                        if (Directory.GetFiles(Path.Combine(versionDir, "bin"), "cudart64_*.dll").Length > 0)
                            return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static async Task<bool> DownloadAndRunCudaInstallerAsync(MainWindow mainWindow)
        {
            EngineDownloadWindow? downloadWindow = null;
            try
            {
                downloadWindow = new EngineDownloadWindow("CUDA Toolkit 12.4 Installer");
                downloadWindow.Owner = mainWindow;
                downloadWindow.Show();

                string url = "https://developer.download.nvidia.com/compute/cuda/12.4.1/network_installers/cuda_12.4.1_windows_network.exe";
                using var httpClient = new HttpClient();

                using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var contentStream = await response.Content.ReadAsStreamAsync();

                    string tempFile = Path.Combine(Path.GetTempPath(), "cuda_12.4.1_windows_network.exe");
                    using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        var totalRead = 0L;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                var percentage = (double)totalRead / totalBytes * 100.0;
                                downloadWindow.UpdateProgress(percentage, $"Baixando instalador do CUDA: {percentage:F0}% ({(double)totalRead / 1024 / 1024:F1}MB / {(double)totalBytes / 1024 / 1024:F1}MB)");
                            }
                            else
                            {
                                downloadWindow.UpdateProgress(50, $"Baixando instalador do CUDA: {(double)totalRead / 1024 / 1024:F1}MB");
                            }
                        }
                    }

                    downloadWindow.UpdateProgress(100, "Iniciando instalador oficial do CUDA Toolkit...");
                    await Task.Delay(500);
                    downloadWindow.Close();
                    downloadWindow = null;

                    Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
                    Process.Start(new ProcessStartInfo("https://developer.nvidia.com/cudnn") { UseShellExecute = true });

                    MessageBox.Show(
                        "O instalador do CUDA Toolkit 12.4 foi iniciado e o site oficial do cuDNN foi aberto no seu navegador.\n\n" +
                        "Após concluir as duas instalações, reinicie o VS AI GLOBAL para ativar o motor CUDA.",
                        "Instalações Iniciadas",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return true;
                }
            }
            catch (Exception ex)
            {
                if (downloadWindow != null)
                {
                    try { downloadWindow.Close(); } catch { }
                }
                MessageBox.Show($"Falha ao baixar o instalador do CUDA:\n{ex.Message}", "Erro de Download", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public static async Task HandleEngineChangeAsync(string selectedEngineStr, MainWindow mainWindow)
        {
            if (!Enum.TryParse<AIEngine>(selectedEngineStr, true, out var selectedEngine))
            {
                LogManager.Log(LogLevel.Error, $"Engine inválido selecionado: {selectedEngineStr}", true);
                return;
            }

            // Para CUDA/TensorRT: verificar CUDA Toolkit E se o provider DLL foi baixado
            if (selectedEngine == AIEngine.CUDA || selectedEngine == AIEngine.TensorRT)
            {
                if (!IsCudaInstalled())
                {
                    var result = MessageBox.Show(
                        "O CUDA Toolkit da NVIDIA não foi encontrado no seu sistema.\n\n" +
                        "Para usar CUDA ou TensorRT, você precisa instalar:\n" +
                        "  1. CUDA Toolkit 12.x (NVIDIA)\n" +
                        "  2. cuDNN (NVIDIA)\n\n" +
                        "Deseja baixar e executar o instalador do CUDA Toolkit 12.4 agora?\n\n" +
                        "(A página do cuDNN também será aberta no navegador.)",
                        "CUDA Toolkit Necessário",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        bool installerSuccess = await DownloadAndRunCudaInstallerAsync(mainWindow);
                        if (!installerSuccess)
                        {
                            mainWindow.Dispatcher.Invoke(() =>
                                mainWindow.SettingsMenuControlInstance?.ResetEngineSelectionToCurrent());
                            return;
                        }
                    }
                    else
                    {
                        mainWindow.Dispatcher.Invoke(() =>
                            mainWindow.SettingsMenuControlInstance?.ResetEngineSelectionToCurrent());
                        return;
                    }
                }

                // Verifica se o provider DLL do CUDA já está na pasta do app
                if (!IsEngineDownloaded(selectedEngine))
                {
                    await DownloadCudaProviderAsync(selectedEngineStr, selectedEngine, mainWindow);
                    // DownloadCudaProviderAsync salva config e reinicia — retorna aqui
                    return;
                }
            }

            // Para OpenVINO: baixar o pacote separado da Intel se ainda não estiver disponível
            if (selectedEngine == AIEngine.OpenVINO && !IsEngineDownloaded(selectedEngine))
            {
                await DownloadOpenVINOProviderAsync(selectedEngineStr, selectedEngine, mainWindow);
                return;
            }

            // Salva configuração
            Dictionary.dropdownState["AI Engine"] = selectedEngineStr;
            SaveDictionary.WriteJSON(Dictionary.dropdownState, "bin\\dropdown.cfg");

            if (IsOnnxRuntimeLoaded())
            {
                var result = MessageBox.Show(
                    $"Motor {selectedEngineStr} selecionado.\n\n" +
                    "Como a IA já está ativa nesta sessão, a alteração só entra em vigor após reiniciar.\n\n" +
                    "Deseja reiniciar agora?",
                    "Reinicialização Necessária",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                    RestartApplication();
            }
            else
            {
                new NoticeBar($"Motor {selectedEngineStr} selecionado! Carregue um modelo para ativar.", 4000).Show();
            }
        }

        private static async Task DownloadCudaProviderAsync(string selectedEngineStr, AIEngine selectedEngine, MainWindow mainWindow)
        {
            EngineDownloadWindow? downloadWindow = null;
            bool success = false;
            string errorMessage = string.Empty;

            try
            {
                downloadWindow = new EngineDownloadWindow(selectedEngineStr);
                downloadWindow.Owner = mainWindow;
                downloadWindow.Show();

                string destDir = AppDomain.CurrentDomain.BaseDirectory;
                string url = EngineUrls[selectedEngine];
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(15);

                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var contentStream = await response.Content.ReadAsStreamAsync();

                string tempFile = Path.Combine(Path.GetTempPath(), $"{selectedEngine}_provider.nupkg");
                using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[81920];
                    var totalRead = 0L;
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                        if (totalBytes > 0)
                        {
                            var pct = (double)totalRead / totalBytes * 100.0;
                            downloadWindow.UpdateProgress(pct * 0.85, $"Baixando CUDA provider: {pct:F0}% ({totalRead / 1024 / 1024:F0}MB / {totalBytes / 1024 / 1024:F0}MB)");
                        }
                        else
                        {
                            downloadWindow.UpdateProgress(40, $"Baixando CUDA provider: {totalRead / 1024 / 1024:F0}MB...");
                        }
                    }
                }

                downloadWindow.UpdateProgress(85, "Extraindo provider CUDA...");

                using (var archive = ZipFile.OpenRead(tempFile))
                {
                    // Extrai apenas os DLLs do provider CUDA (não o onnxruntime.dll que já está no app)
                    var entries = archive.Entries
                        .Where(e => e.FullName.StartsWith("runtimes/win-x64/native/", StringComparison.OrdinalIgnoreCase))
                        .Where(e => CudaProviderDlls.Contains(Path.GetFileName(e.FullName), StringComparer.OrdinalIgnoreCase))
                        .ToList();

                    if (entries.Count == 0)
                        throw new InvalidOperationException("Provider DLLs do CUDA não encontrados no pacote.");

                    int count = 0;
                    foreach (var entry in entries)
                    {
                        string fileName = Path.GetFileName(entry.FullName);
                        entry.ExtractToFile(Path.Combine(destDir, fileName), true);
                        count++;
                        downloadWindow.UpdateProgress(85 + (double)count / entries.Count * 15, $"Extraindo {fileName}...");
                    }

                    LogManager.Log(LogLevel.Info, $"[CUDA] Extraídos {count} provider DLLs para {destDir}");
                }

                File.Delete(tempFile);
                success = true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                LogManager.Log(LogLevel.Error, $"Erro ao baixar provider CUDA: {ex}", true);
            }
            finally
            {
                try { downloadWindow?.Close(); } catch { }
            }

            if (!success)
            {
                MessageBox.Show($"Falha ao baixar o provider CUDA:\n{errorMessage}",
                    "Erro de Download", MessageBoxButton.OK, MessageBoxImage.Error);
                mainWindow.Dispatcher.Invoke(() =>
                    mainWindow.SettingsMenuControlInstance?.ResetEngineSelectionToCurrent());
                return;
            }

            // Salva config e reinicia para ativar o engine
            Dictionary.dropdownState["AI Engine"] = selectedEngineStr;
            SaveDictionary.WriteJSON(Dictionary.dropdownState, "bin\\dropdown.cfg");

            var restart = MessageBox.Show(
                $"Provider {selectedEngineStr} baixado com sucesso!\n\n" +
                "O aplicativo precisa reiniciar para ativar o motor CUDA.\n\n" +
                "Deseja reiniciar agora?",
                "Reinicialização Necessária",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (restart == MessageBoxResult.Yes)
                RestartApplication();
        }

        private static async Task DownloadOpenVINOProviderAsync(string selectedEngineStr, AIEngine selectedEngine, MainWindow mainWindow)
        {
            EngineDownloadWindow? downloadWindow = null;
            bool success = false;
            string errorMessage = string.Empty;

            try
            {
                downloadWindow = new EngineDownloadWindow(selectedEngineStr);
                downloadWindow.Owner = mainWindow;
                downloadWindow.Show();

                string destDir = GetEngineDirectory(selectedEngine);
                Directory.CreateDirectory(destDir);

                string url = EngineUrls[selectedEngine];
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(10);

                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var contentStream = await response.Content.ReadAsStreamAsync();

                string tempFile = Path.Combine(Path.GetTempPath(), "openvino_download.nupkg");
                using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    var totalRead = 0L;
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                        if (totalBytes > 0)
                        {
                            var pct = (double)totalRead / totalBytes * 100.0;
                            downloadWindow.UpdateProgress(pct * 0.8, $"Baixando: {pct:F0}% ({totalRead / 1024 / 1024:F1}MB / {totalBytes / 1024 / 1024:F1}MB)");
                        }
                        else
                        {
                            downloadWindow.UpdateProgress(40, $"Baixando: {totalRead / 1024 / 1024:F1}MB...");
                        }
                    }
                }

                downloadWindow.UpdateProgress(80, "Extraindo arquivos nativos...");

                using (var archive = ZipFile.OpenRead(tempFile))
                {
                    var entries = archive.Entries
                        .Where(e => e.FullName.StartsWith("runtimes/win-x64/native/", StringComparison.OrdinalIgnoreCase))
                        .Where(e => !string.IsNullOrEmpty(Path.GetFileName(e.FullName)))
                        .ToList();

                    if (entries.Count == 0)
                        throw new InvalidOperationException("Pacote OpenVINO não contém DLLs nativas.");

                    int count = 0;
                    foreach (var entry in entries)
                    {
                        entry.ExtractToFile(Path.Combine(destDir, Path.GetFileName(entry.FullName)), true);
                        count++;
                        downloadWindow.UpdateProgress(80 + (double)count / entries.Count * 15, $"Extraindo {Path.GetFileName(entry.FullName)}...");
                    }
                }

                // Cópia das libs do MOTOR_INTEL se existir
                string parentDir = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!;
                string openvinoLibs = Path.Combine(parentDir, "MOTOR_INTEL", "Lib", "site-packages", "openvino", "libs");
                if (!Directory.Exists(openvinoLibs))
                    openvinoLibs = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MOTOR_INTEL", "Lib", "site-packages", "openvino", "libs");

                if (Directory.Exists(openvinoLibs))
                {
                    downloadWindow.UpdateProgress(95, "Copiando bibliotecas do MOTOR_INTEL...");
                    foreach (var file in Directory.GetFiles(openvinoLibs, "*.dll"))
                        File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
                }

                File.Delete(tempFile);
                success = true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                LogManager.Log(LogLevel.Error, $"Erro ao baixar OpenVINO: {ex}", true);
            }
            finally
            {
                try { downloadWindow?.Close(); } catch { }
            }

            if (!success)
            {
                MessageBox.Show($"Falha ao baixar o motor OpenVINO:\n{errorMessage}",
                    "Erro de Download", MessageBoxButton.OK, MessageBoxImage.Error);
                mainWindow.Dispatcher.Invoke(() =>
                    mainWindow.SettingsMenuControlInstance?.ResetEngineSelectionToCurrent());
                return;
            }

            Dictionary.dropdownState["AI Engine"] = selectedEngineStr;
            SaveDictionary.WriteJSON(Dictionary.dropdownState, "bin\\dropdown.cfg");

            var restart = MessageBox.Show(
                "Motor OpenVINO configurado com sucesso!\n\n" +
                "O aplicativo precisa reiniciar para ativar o motor.\n\n" +
                "Deseja reiniciar agora?",
                "Reinicialização Necessária",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (restart == MessageBoxResult.Yes)
                RestartApplication();
        }

        private static void RestartApplication()
        {
            try
            {
                string? executablePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
                Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Error, $"Falha ao reiniciar o aplicativo: {ex.Message}", true);
            }
        }
    }
}
