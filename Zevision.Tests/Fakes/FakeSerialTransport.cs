using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Communication;

namespace Zevision.Tests
{
    /// <summary>
    /// 内存中的串口假实现。写入的帧可以由 <see cref="Responder"/> 自动应答。
    /// </summary>
    internal sealed class FakeSerialTransport : IDeviceTransport
    {
        private readonly object _sync = new object();
        private readonly Queue<byte[]> _incoming = new Queue<byte[]>();
        private TaskCompletionSource<int>? _pendingRead;
        private byte[]? _pendingBuffer;
        private Exception? _failure;

        public bool IsOpen { get; private set; }

        public List<byte[]> Written { get; } = new List<byte[]>();

        /// <summary>收到写入的帧后返回要回送的字节;返回 null 表示不应答。</summary>
        public Func<byte[], byte[]?>? Responder { get; set; }

        public Exception? OpenException { get; set; }

        public Task OpenAsync(ConnectionEndpoint endpoint, CancellationToken cancellationToken)
        {
            if (OpenException != null)
            {
                throw OpenException;
            }

            IsOpen = true;
            return Task.CompletedTask;
        }

        public void Close()
        {
            TaskCompletionSource<int>? pending;
            lock (_sync)
            {
                IsOpen = false;
                pending = _pendingRead;
                _pendingRead = null;
            }

            pending?.TrySetResult(0);
        }

        public Task WriteAsync(byte[] data, CancellationToken cancellationToken)
        {
            Written.Add(data);
            var reply = Responder?.Invoke(data);
            if (reply != null)
            {
                Inject(reply);
            }

            return Task.CompletedTask;
        }

        public Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_failure != null)
                {
                    return Task.FromException<int>(_failure);
                }

                if (!IsOpen)
                {
                    return Task.FromResult(0);
                }

                if (_incoming.Count > 0)
                {
                    return Task.FromResult(Copy(_incoming.Dequeue(), buffer));
                }

                _pendingBuffer = buffer;
                _pendingRead = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _pendingRead.Task;
            }
        }

        /// <summary>模拟设备发来数据。</summary>
        public void Inject(byte[] data)
        {
            TaskCompletionSource<int>? pending;
            byte[]? buffer;
            lock (_sync)
            {
                pending = _pendingRead;
                buffer = _pendingBuffer;
                if (pending == null || buffer == null)
                {
                    _incoming.Enqueue(data);
                    return;
                }

                _pendingRead = null;
                _pendingBuffer = null;
            }

            pending.TrySetResult(Copy(data, buffer));
        }

        /// <summary>模拟读取失败 (默认为拔掉串口线的 IOException)。</summary>
        public void SimulateFailure(Exception? failure = null)
        {
            TaskCompletionSource<int>? pending;
            lock (_sync)
            {
                _failure = failure ?? new IOException("设备已移除。");
                pending = _pendingRead;
                _pendingRead = null;
            }

            pending?.TrySetException(_failure);
        }

        public void Dispose() => Close();

        private static int Copy(byte[] source, byte[] destination)
        {
            Buffer.BlockCopy(source, 0, destination, 0, source.Length);
            return source.Length;
        }
    }
}
