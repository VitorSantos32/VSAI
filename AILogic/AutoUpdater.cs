using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Other;

namespace VSAI.AILogic
{
    public static class AutoUpdater
    {
        public const string CurrentVersion = "2.5.0";
        private const string DefaultUpdateUrl = "https://raw.githubusercontent.com/VitorSantos32/VSAI/main/update.json";
        private static readonly string ConfigPath = Path.Combine("bin", "updater.cfg");

        public static async Task CheckForUpdatesAsync(StartupWindow startupWindow)
        {
            string updateUrl = DefaultUpdateUrl;

            try
            {
                // Garante que a pasta bin existe
                string binDir = Path.GetDirectoryName(ConfigPath) ?? "bin";
                if (!Directory.Exists(binDir))
                {
                    Directory.CreateDirectory(binDir);
                }

                // Carrega ou cria a URL de atualização configurável
                if (File.Exists(ConfigPath))
                {
                    string savedUrl = File.ReadAllText(ConfigPath).Trim();
                    if (!string.IsNullOrEmpty(savedUrl))
                    {
                        updateUrl = savedUrl;
                    }
                }
                else
                {
                    File.WriteAllText(ConfigPath, DefaultUpdateUrl);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warning, $"[Updater] Falha ao ler/criar config: {ex.Message}");
            }

            try
            {
                // Atualiza o texto na interface
                await startupWindow.Dispatcher.InvokeAsync(() => startupWindow.LoadingText.Text = "VERIFICANDO ATUALIZAÇÕES");

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(8);

                string jsonContent = await httpClient.GetStringAsync(updateUrl);
                var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (updateInfo == null || string.IsNullOrEmpty(updateInfo.Version) || string.IsNullOrEmpty(updateInfo.Url))
                {
                    LogManager.Log(LogManager.LogLevel.Warning, "[Updater] Arquivo de atualização inválido ou incompleto.");
                    return;
                }

                // Compara as versões
                if (Version.TryParse(updateInfo.Version, out var remoteVersion) &&
                    Version.TryParse(CurrentVersion, out var localVersion))
                {
                    if (remoteVersion > localVersion)
                    {
                        // Encontrou nova versão! Pergunta se deseja atualizar
                        bool shouldUpdate = false;
                        await startupWindow.Dispatcher.InvokeAsync(() =>
                        {
                            var result = MessageBox.Show(
                                $"Uma nova versão (v{updateInfo.Version}) está disponível!\n\n" +
                                $"Notas da versão:\n{updateInfo.Changelog}\n\n" +
                                "Deseja baixar e instalar agora?",
                                "Atualização Disponível",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);
                            shouldUpdate = result == MessageBoxResult.Yes;
                        });

                        if (shouldUpdate)
                        {
                            await DownloadAndApplyUpdateAsync(updateInfo.Url, startupWindow);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, $"[Updater] Falha ao verificar atualizações: {ex.Message}");
                // Falhas na verificação não devem impedir a inicialização do app
            }
        }

        private static async Task DownloadAndApplyUpdateAsync(string downloadUrl, StartupWindow startupWindow)
        {
            string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update_temp.zip");
            string batchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updater.bat");

            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(10);

                using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var contentStream = await response.Content.ReadAsStreamAsync();

                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
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
                            await startupWindow.Dispatcher.InvokeAsync(() =>
                            {
                                startupWindow.LoadingText.Text = $"BAIXANDO ATUALIZAÇÃO: {pct:F0}% ({(double)totalRead / 1024 / 1024:F1}MB / {(double)totalBytes / 1024 / 1024:F1}MB)";
                            });
                        }
                        else
                        {
                            await startupWindow.Dispatcher.InvokeAsync(() =>
                            {
                                startupWindow.LoadingText.Text = $"BAIXANDO ATUALIZAÇÃO: {(double)totalRead / 1024 / 1024:F1}MB";
                            });
                        }
                    }
                }

                await startupWindow.Dispatcher.InvokeAsync(() => startupWindow.LoadingText.Text = "PREPARANDO INSTALAÇÃO");
                await Task.Delay(500);

                // Cria o script batch do updater inteligente
                string batchContent = $@"@echo off
title Atualizando VS AI...
echo Aguardando o aplicativo fechar...
timeout /t 3 /nobreak > nul
echo Instalando atualizacao...

if exist update_extracted rd /s /q update_extracted
powershell -Command ""Expand-Archive -Path 'update_temp.zip' -DestinationPath 'update_extracted' -Force""
if exist ""update_temp.zip"" del ""update_temp.zip""

:: Determina o diretorio de origem correto (trata ZIPs compactados com pasta pai)
set ""TARGET_DIR=update_extracted""
set ""DIR_COUNT=0""
for /d %%d in (update_extracted\*) do set /a DIR_COUNT+=1
set ""FILE_COUNT=0""
for %%f in (update_extracted\*) do set /a FILE_COUNT+=1

if %FILE_COUNT%==0 (
    if %DIR_COUNT%==1 (
        for /d %%d in (update_extracted\*) do set ""TARGET_DIR=%%d""
    )
)

:: Copia e substitui todos os arquivos da atualizacao para a pasta raiz
xcopy ""%TARGET_DIR%\*"" ""."" /s /e /y /q > nul
rd /s /q update_extracted

echo Atualizacao concluida! Reiniciando...
start """" ""VS AI.exe""
del ""%~f0""";

                await File.WriteAllTextAsync(batchPath, batchContent);

                // Executa o script
                Process.Start(new ProcessStartInfo
                {
                    FileName = batchPath,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = true,
                    CreateNoWindow = false
                });

                // Encerra a aplicação
                await startupWindow.Dispatcher.InvokeAsync(() =>
                {
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, $"[Updater] Falha ao instalar atualização: {ex.Message}", true);
                await startupWindow.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(
                        $"Erro ao baixar ou instalar a atualização:\n{ex.Message}\n\nO aplicativo continuará a inicialização normal.",
                        "Erro de Atualização",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });

                // Limpa arquivos temporários em caso de falha
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                try { if (File.Exists(batchPath)) File.Delete(batchPath); } catch { }
            }
        }

        private class UpdateInfo
        {
            public string? Version { get; set; }
            public string? Url { get; set; }
            public string? Changelog { get; set; }
        }
    }
}
