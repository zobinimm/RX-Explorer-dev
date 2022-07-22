﻿using Microsoft.Toolkit.Deferred;
using SharedLibrary;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RX_Explorer.Class
{
    public class NamedPipeReadController : NamedPipeControllerBase
    {
        private CancellationTokenSource Cancellation;
        private readonly Thread ProcessThread;
        private readonly TaskCompletionSource<bool> ConnectionSet;
        public event EventHandler<NamedPipeDataReceivedArgs> OnDataReceived;

        protected override int MaxAllowedConnection => 1;

        private void ReadProcess()
        {
            try
            {
                if (!IsConnected)
                {
                    using (CancellationTokenSource LocalCancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation.Token))
                    {
                        try
                        {
                            PipeStream.WaitForConnectionAsync(LocalCancellation.Token).Wait();
                        }
                        catch (AggregateException ex) when (ex.InnerException is IOException)
                        {
                            LogTracer.Log("Could not read pipeline data because the pipeline is closed");
                        }
                        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
                        {
                            LogTracer.Log("Could not read pipeline data because connection timeout");
                        }
                        catch (Exception ex)
                        {
                            LogTracer.Log(ex, "Could not read pipeline data because unknown exception");
                        }
                    }
                }

                ConnectionSet.SetResult(IsConnected);

                while (IsConnected)
                {
                    using (MemoryStream MStream = new MemoryStream())
                    {
                        try
                        {
                            byte[] ReadBuffer = new byte[1024];

                            do
                            {
                                int BytesRead = PipeStream.Read(ReadBuffer, 0, ReadBuffer.Length);

                                if (BytesRead > 0)
                                {
                                    MStream.Write(ReadBuffer, 0, BytesRead);
                                }
                            } while (IsConnected && !PipeStream.IsMessageComplete);
                        }
                        catch (ObjectDisposedException)
                        {
                            //No need to handle this exception which raised when Dispose() is called
                            break;
                        }

                        string ReadText = Encoding.Unicode.GetString(MStream.ToArray());

                        if (!string.IsNullOrEmpty(ReadText))
                        {
                            OnDataReceived?.InvokeAsync(this, new NamedPipeDataReceivedArgs(ReadText)).Wait();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnDataReceived?.InvokeAsync(this, new NamedPipeDataReceivedArgs(ex)).Wait();
            }
        }

        public override async Task<bool> WaitForConnectionAsync(int TimeoutMilliseconds)
        {
            if (ConnectionSet.Task.IsCompleted)
            {
                return true;
            }
            else
            {
                if (await Task.WhenAny(ConnectionSet.Task, Task.Delay(TimeoutMilliseconds)) == ConnectionSet.Task)
                {
                    return ConnectionSet.Task.Result;
                }
                else
                {
                    Cancellation?.Cancel();
                }
            }

            return false;
        }

        public NamedPipeReadController() : this($"Explorer_NamedPipe_Read_{Guid.NewGuid():D}")
        {

        }

        protected NamedPipeReadController(string Id) : base(Id)
        {
            Cancellation = new CancellationTokenSource();
            ConnectionSet = new TaskCompletionSource<bool>();

            ProcessThread = new Thread(ReadProcess)
            {
                Priority = ThreadPriority.Normal,
                IsBackground = true
            };
            ProcessThread.Start();
        }

        public override void Dispose()
        {
            if (!IsDisposed)
            {
                base.Dispose();
                Cancellation.Dispose();
                Cancellation = null;
            }
        }

        ~NamedPipeReadController()
        {
            Dispose();
        }
    }
}
