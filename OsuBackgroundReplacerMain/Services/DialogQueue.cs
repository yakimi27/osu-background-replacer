using System;
using System.Threading;
using System.Threading.Tasks;

namespace OsuBackgroundReplacerMain.Services
{
    /// <summary>
    /// Coordinates dialog presentations to ensure only one dialog is shown at a time.
    /// In WinUI 3, attempting to open a ContentDialog while one is already active throws an exception.
    /// </summary>
    public class DialogQueue
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public async Task<T> EnqueueAsync<T>(Func<Task<T>> showDialogFunc)
        {
            ArgumentNullException.ThrowIfNull(showDialogFunc);

            await _semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                return await showDialogFunc().ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
