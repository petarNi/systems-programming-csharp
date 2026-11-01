using System.Reactive.Concurrency;
using System.Reactive.Linq;
using RxFootballApp.Models;
using RxFootballApp.Services;

namespace RxFootballApp.Rx
{
    public class RxProcessor
    {
        private readonly SportMonksClient client = new();

        /// <summary>
        /// Builds a reactive stream that fetches several fixtures in parallel.
        /// Requests run on the ThreadPool, results are observed on a dedicated thread.
        /// </summary>
        public IObservable<FixtureInfo> FetchFixtures(IEnumerable<int> fixtureIds)
        {
            return fixtureIds
                .ToObservable()
                .SelectMany(id => Observable
                    .FromAsync(() => client.GetFixtureAsync(id))
                    .SubscribeOn(ThreadPoolScheduler.Instance)
                    .Do(_ => Logger.Log($"Fixture {id} received"))
                    .Catch<FixtureInfo, Exception>(ex =>
                    {
                        Logger.Log($"Error for fixture {id}: {ex.Message}");
                        return Observable.Empty<FixtureInfo>();
                    }))
                .ObserveOn(NewThreadScheduler.Default);
        }

        public async Task RunAsync(IEnumerable<int> fixtureIds)
        {
            int received = 0;
            var done = new TaskCompletionSource();

            using var subscription = FetchFixtures(fixtureIds).Subscribe(
                onNext: fixture =>
                {
                    received++;
                    Console.WriteLine();
                    Console.WriteLine(fixture);
                    Console.WriteLine();
                },
                onError: ex =>
                {
                    Logger.Log("Stream failed: " + ex.Message);
                    done.TrySetResult();
                },
                onCompleted: () =>
                {
                    Logger.Log($"Stream completed, {received} fixture(s) displayed");
                    done.TrySetResult();
                });

            await done.Task;
        }
    }
}
