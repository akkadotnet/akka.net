using Akka.Actor;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Akka.Hosting.LoggingDemo;

public class WorkerService: IHostedService
{
    private readonly ILogger<WorkerService> _logger;
    private readonly IRequiredActor<Echo> _reqEcho;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Task? _timerTask;

    public WorkerService(IRequiredActor<Echo> reqActor, ILogger<WorkerService> logger)
    {
        _reqEcho = reqActor;
        _logger = logger;
        _cancellationTokenSource = new CancellationTokenSource();
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timerTask = StartTimerTask();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource.Cancel();
        if(_timerTask != null)
            await _timerTask;
    }

    private async Task StartTimerTask()
    {
        var echoActor = await _reqEcho.GetAsync();
        var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        try
        {
            while (true)
            {
                if (await periodicTimer.WaitForNextTickAsync(_cancellationTokenSource.Token))
                {
                    var response = await echoActor.Ask<string>(Guid.NewGuid().ToString(), _cancellationTokenSource.Token);
                    _logger.LogInformation(response);
                }
            }
        }
        catch
        {
            // no-op
        }
    }
}