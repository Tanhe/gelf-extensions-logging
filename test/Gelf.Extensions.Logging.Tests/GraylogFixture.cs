﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Gelf.Extensions.Logging.Tests
{
    public class GraylogFixture : IAsyncLifetime
    {
        public static readonly string GraylogHost = Environment.GetEnvironmentVariable("GRAYLOG_HOST") ?? "localhost";
        public const int GraylogLogPort = 12201;
        private const string GraylogUsername = "admin";
        private const string GraylogPassword = "admin";
        private const int GraylogApiPort = 9000;
        private const int ApiPollInterval = 200;
        private const int ApiPollTimeout = 10000;

        private readonly HttpClientWrapper _httpClient;

        public GraylogFixture()
        {
            _httpClient = new HttpClientWrapper(
                $"http://{GraylogHost}:{GraylogApiPort}/api/", GraylogUsername, GraylogPassword);
        }

        public async Task InitializeAsync()
        {
            await WaitForGraylogAsync();
            var inputId = await CreateUdpInputAsync();
            await WaitForUdpInputAsync(inputId);
        }

        private Task WaitForGraylogAsync()
        {
            return RepeatUntilAsync(async cancellation =>
            {
                try
                {
                    await _httpClient.GetAsync("system/stats", cancellation);
                    return true;
                }
                catch (HttpRequestException)
                {
                    return false;
                }
            }, retryInterval: 2000, retryTimeout: 60000);
        }

        private async Task<string> CreateUdpInputAsync()
        {
            List<dynamic> existingInputs = (await _httpClient.GetAsync("system/inputs")).inputs;
            var input = existingInputs.SingleOrDefault(i => i.attributes.port == GraylogLogPort);
            if (input != null)
            {
                return input.id;
            }

            var newInputRequest = new
            {
                title = "Gelf.Extensions.Logging.Tests",
                global = true,
                type = "org.graylog2.inputs.gelf.udp.GELFUDPInput",
                configuration = new
                {
                    bind_address = "0.0.0.0",
                    decompress_size_limit = 8388608,
                    override_source = default(object),
                    port = GraylogLogPort,
                    recv_buffer_size = 262144
                }
            };

            var newInputResponse = await _httpClient.PostAsync(newInputRequest, "system/inputs");
            return newInputResponse.id;
        }

        private Task WaitForUdpInputAsync(string inputId)
        {
            return RepeatUntilAsync(async cancellation =>
                await _httpClient.GetAsync($"system/inputstates/{inputId}", cancellation) != null);
        }

        public async Task<List<dynamic>> WaitForMessagesAsync(int count = 1)
        {
            var query = $"test_id:\"{TestContext.TestId}\"";
            var url = $"search/universal/relative?query={WebUtility.UrlEncode(query)}&range=60";

            var messages = new List<dynamic>();

            await RepeatUntilAsync(async cancellation =>
            {
                messages.AddRange((await _httpClient.GetAsync(url, cancellation)).messages);
                return messages.Count == count;
            });

            return messages.Select(m => m.message).ToList();
        }

        public async Task<dynamic> WaitForMessageAsync()
        {
            return (await WaitForMessagesAsync()).Single();
        }

        private static async Task RepeatUntilAsync(Func<CancellationToken, Task<bool>> predicate,
            int retryInterval = ApiPollInterval, int retryTimeout = ApiPollTimeout)
        {
            using (var cts = new CancellationTokenSource(retryTimeout))
            {
                while (!await predicate(cts.Token))
                {
                    await Task.Delay(retryInterval, cts.Token);
                }
            }
        }

        public Task DisposeAsync()
        {
            _httpClient.Dispose();
            return Task.CompletedTask;
        }
    }
}
