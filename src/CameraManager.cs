using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Scopie
{
    public class CameraManager : Camera
    {
        private static readonly ConcurrentQueue<(Camera camera, TaskCompletionSource<ushort[]> task, int exposure)> _waitQueue =
            new ConcurrentQueue<(Camera, TaskCompletionSource<ushort[]>, int)>();
        private static Thread _checkingThread;

        public CameraManager(int cameraIndex)
            : base(cameraIndex)
        {
        }

        static void RunThread()
        {
            var waiters = new List<(Camera camera, TaskCompletionSource<ushort[]> task)>();
            var toRemove = new List<(Camera camera, TaskCompletionSource<ushort[]> task)>();
            while (true)
            {
                while (_waitQueue.TryDequeue(out var newWaiter))
                {
                    var status = newWaiter.camera.ExposureStatus;
                    if (status != ASICameraDll.ExposureStatus.ExpIdle && status != ASICameraDll.ExposureStatus.ExpFailed)
                    {
                        newWaiter.task.SetException(new Exception("Camera already exposing"));
                        continue;
                    }
                    var exposure = newWaiter.camera.GetControl(ASICameraDll.ASI_CONTROL_TYPE.ASI_EXPOSURE);
                    if (exposure.Value != newWaiter.exposure)
                    {
                        exposure.Value = newWaiter.exposure;
                    }
                    newWaiter.camera.StartExposure(false);
                    waiters.Add((newWaiter.camera, newWaiter.task));
                }
                foreach (var waiter in waiters)
                {
                    var status = waiter.camera.ExposureStatus;
                    switch (status)
                    {
                        case ASICameraDll.ExposureStatus.ExpWorking:
                            break;
                        case ASICameraDll.ExposureStatus.ExpSuccess:
                            var buffer = ArrayPool<ushort>.Alloc(waiter.camera.Width * waiter.camera.Height);
                            waiter.camera.GetExposureData(buffer);
                            waiter.task.SetResult(buffer);
                            toRemove.Add(waiter);
                            break;
                        case ASICameraDll.ExposureStatus.ExpFailed:
                            waiter.task.SetException(new Exception("Exposure failed"));
                            toRemove.Add(waiter);
                            break;
                        default:
                            waiter.task.SetException(new Exception("Invalid camera state: " + status));
                            break;
                    }
                }
                if (toRemove.Count > 0)
                {
                    foreach (var waiter in toRemove)
                    {
                        waiters.Remove(waiter);
                    }
                    toRemove.Clear();
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }

        private static void EnsureThreadRunning()
        {
            if (_checkingThread == null && Interlocked.CompareExchange(ref _checkingThread, new Thread(RunThread), null) == null)
            {
                _checkingThread.IsBackground = true;
                _checkingThread.Start();
            }
        }

        public Task<ushort[]> Expose(int microseconds)
        {
            EnsureThreadRunning();
            var task = new TaskCompletionSource<ushort[]>();
            _waitQueue.Enqueue((this, task, microseconds));
            return task.Task;
        }
    }
}
