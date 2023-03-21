using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using StackExchange.Redis;
using NLog;
using NLog.Extensions.Hosting;
using NLog.Extensions.Logging;
using RabbitMQ.Client.Events;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Configuration;

namespace RabbitMqToRedis
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(System.IO.Directory.GetCurrentDirectory()) //From NuGet Package Microsoft.Extensions.Configuration.Json
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            var builder = new HostBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    var rabbitMqConnection = new ConnectionFactory
                    {
                        HostName = args[0],
                        UserName = args[1],
                        Password = args[2],
                        VirtualHost = args[3]
                    }.CreateConnection();

                    var redisConnection = ConnectionMultiplexer.Connect($"{args[5]},abortConnect=true");

                    services.AddSingleton(rabbitMqConnection);
                    services.AddSingleton(redisConnection);
                    services.AddHostedService<Worker>();
                    services.AddSingleton(new QueueConfig()
                    {
                        queueName = args[4],
                        pubsubName = args[6]
                    });
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                    logging.AddNLog(config);
                })
                .UseNLog();

            await builder.RunConsoleAsync();
        }
    }

    public class QueueConfig
    {
        public string? queueName { get; set; }
        public string? pubsubName { get; set; }
    }

    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConnection _rabbitMqConnection;
        private readonly ISubscriber _redisSubscriber;
        private readonly string _queueName;
        private readonly ManualResetEvent resetEvent;
        private readonly string _pubSubName;

        public Worker(
            ILogger<Worker> logger,
            IConnection rabbitMqConnection,
            ConnectionMultiplexer redisConnection,
            QueueConfig queueConfig)
        {
            _logger = logger;
            _rabbitMqConnection = rabbitMqConnection;
            _redisSubscriber = redisConnection.GetSubscriber();
            _queueName = queueConfig.queueName ?? "";
            _pubSubName = queueConfig.pubsubName ?? "";
            resetEvent = new ManualResetEvent(false);

        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var channel = _rabbitMqConnection.CreateModel();
                    channel.BasicQos(0, 1, false);
                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (model, ea) =>
                    {
                        var body = ea.Body.ToArray();
                        var message = System.Text.Encoding.UTF8.GetString(body);
                        _logger.LogInformation("Received message: {0}", message);
                        _redisSubscriber.PublishAsync(_pubSubName, message);
                        channel.BasicAck(ea.DeliveryTag, false);
                    };
                    channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
                    resetEvent.WaitOne(); //one day actually add cancellation feature
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in RabbitMqToRedis worker.");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("RabbitMqToRedis worker is stopping.");
            _rabbitMqConnection.Close();
            _redisSubscriber.UnsubscribeAll();
            return base.StopAsync(cancellationToken);
        }
    }
}