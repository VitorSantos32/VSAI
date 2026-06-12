using Microsoft.ML.OnnxRuntime;
using Other;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace VSAI.AILogic
{
    internal static class OnnxModelSessionFactory
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string lpPathName);

        private static SessionOptions CreateDefaultOptions()
        {
            return new SessionOptions
            {
                EnableCpuMemArena = true,
                EnableMemoryPattern = true,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                IntraOpNumThreads = 0
            };
        }

        /// <summary>
        /// Localiza os diretórios do CUDA Toolkit e cuDNN no sistema,
        /// registra via AddDllDirectory e pré-carrega as DLLs na ordem correta.
        /// </summary>
        private static void PreloadCudaDependencies()
        {
            var cudaDirs = FindCudaDirectories();

            foreach (var dir in cudaDirs)
            {
                try { AddDllDirectory(dir); } catch { }

                try
                {
                    string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                    if (!currentPath.Contains(dir, StringComparison.OrdinalIgnoreCase))
                        Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + currentPath);
                }
                catch { }
            }

            // Pré-carrega as DLLs do CUDA na ordem correta para que o Windows as encontre
            // quando o onnxruntime_providers_cuda.dll for carregado
            var dllsToPreload = new[]
            {
                "cudart64_12.dll", "cudart64_11.dll",
                "cublas64_12.dll", "cublas64_11.dll",
                "cublasLt64_12.dll", "cublasLt64_11.dll",
                "cudnn64_9.dll", "cudnn64_8.dll",
                "cudnn_ops64_9.dll", "cudnn_ops_infer64_8.dll",
                "cudnn_cnn64_9.dll", "cudnn_cnn_infer64_8.dll",
            };

            foreach (var dir in cudaDirs)
            {
                foreach (var dll in dllsToPreload)
                {
                    string fullPath = Path.Combine(dir, dll);
                    if (File.Exists(fullPath))
                    {
                        try
                        {
                            NativeLibrary.Load(fullPath);
                            LogManager.Log(LogManager.LogLevel.Info, $"[CUDA] Pré-carregado: {dll}");
                        }
                        catch { }
                    }
                }
            }

            // Pré-carrega os providers do onnxruntime na ordem correta
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string sharedProvider = Path.Combine(appDir, "onnxruntime_providers_shared.dll");
            string cudaProvider = Path.Combine(appDir, "onnxruntime_providers_cuda.dll");
            string tensorrtProvider = Path.Combine(appDir, "onnxruntime_providers_tensorrt.dll");

            if (File.Exists(sharedProvider))
            {
                try { NativeLibrary.Load(sharedProvider); } catch { }
            }
            if (File.Exists(cudaProvider))
            {
                try { NativeLibrary.Load(cudaProvider); } catch { }
            }
            if (File.Exists(tensorrtProvider))
            {
                try { NativeLibrary.Load(tensorrtProvider); } catch { }
            }
        }

        private static string[] FindCudaDirectories()
        {
            var dirs = new System.Collections.Generic.List<string>();
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            // 1. CUDA_PATH env var
            string? cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
            if (!string.IsNullOrEmpty(cudaPath) && Directory.Exists(cudaPath))
            {
                string bin = Path.Combine(cudaPath, "bin");
                if (Directory.Exists(bin)) dirs.Add(bin);
            }

            // 2. Pasta padrão do CUDA Toolkit (todas as versões, da mais nova para a mais antiga)
            string cudaBase = Path.Combine(programFiles, "NVIDIA GPU Computing Toolkit", "CUDA");
            if (Directory.Exists(cudaBase))
            {
                foreach (var ver in Directory.GetDirectories(cudaBase).OrderByDescending(d => d))
                {
                    string bin = Path.Combine(ver, "bin");
                    if (Directory.Exists(bin) && !dirs.Contains(bin))
                        dirs.Add(bin);
                }
            }

            // 3. cuDNN - NVIDIA\CUDNN\vX.XX\bin\CUDAXX\x64
            string cudnnBase = Path.Combine(programFiles, "NVIDIA", "CUDNN");
            if (Directory.Exists(cudnnBase))
            {
                foreach (var ver in Directory.GetDirectories(cudnnBase).OrderByDescending(d => d))
                {
                    string bin = Path.Combine(ver, "bin");
                    if (!Directory.Exists(bin)) continue;

                    var subDirs = Directory.GetDirectories(bin);
                    if (subDirs.Length > 0)
                    {
                        foreach (var sub in subDirs.OrderByDescending(d => d))
                        {
                            string x64 = Path.Combine(sub, "x64");
                            if (Directory.Exists(x64) && !dirs.Contains(x64)) dirs.Add(x64);
                            else if (!dirs.Contains(sub)) dirs.Add(sub);
                        }
                    }
                    else if (!dirs.Contains(bin))
                    {
                        dirs.Add(bin);
                    }
                }
            }

            // 4. PATH do sistema (procura por dirs que já têm cudart)
            string? pathVar = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathVar))
            {
                foreach (var p in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        if (Directory.Exists(p) &&
                            Directory.GetFiles(p, "cudart64_*.dll").Length > 0 &&
                            !dirs.Contains(p, StringComparer.OrdinalIgnoreCase))
                        {
                            dirs.Add(p);
                        }
                    }
                    catch { }
                }
            }

            if (dirs.Count > 0)
                LogManager.Log(LogManager.LogLevel.Info, $"[CUDA] Diretórios encontrados: {string.Join("; ", dirs)}");
            else
                LogManager.Log(LogManager.LogLevel.Warning, "[CUDA] CUDA Toolkit não encontrado no sistema. Instale o CUDA Toolkit 12.x.");

            return dirs.ToArray();
        }

        internal static OnnxModelLoadResult Load(string modelPath, AIEngine engine)
        {
            if (engine == AIEngine.CUDA || engine == AIEngine.TensorRT)
            {
                PreloadCudaDependencies();
            }

            using SessionOptions sessionOptions = CreateDefaultOptions();

            switch (engine)
            {
                case AIEngine.DirectML:
                    sessionOptions.AppendExecutionProvider_DML();
                    break;
                case AIEngine.CUDA:
                    sessionOptions.AppendExecutionProvider_CUDA(0);
                    break;
                case AIEngine.TensorRT:
                    sessionOptions.AppendExecutionProvider_Tensorrt(0);
                    break;
                case AIEngine.OpenVINO:
                    sessionOptions.AppendExecutionProvider_OpenVINO("");
                    break;
                case AIEngine.CPU:
                default:
                    sessionOptions.AppendExecutionProvider_CPU();
                    break;
            }

            InferenceSession? session = null;
            try
            {
                session = new InferenceSession(modelPath, sessionOptions);
                var result = new OnnxModelLoadResult(session, new List<string>(session.OutputMetadata.Keys));
                session = null;
                return result;
            }
            finally
            {
                session?.Dispose();
            }
        }
    }

    internal sealed record OnnxModelLoadResult(InferenceSession Session, List<string> OutputNames);
}
