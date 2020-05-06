using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ZoomNet.IntegrationTests.Tests;
using ZoomNet.Utilities;

namespace ZoomNet.IntegrationTests
{
	internal class TestsRunner
	{
		private const int MAX_ZOOM_API_CONCURRENCY = 5;
		private const int TEST_NAME_MAX_LENGTH = 25;
		private const string SUCCESSFUL_TEST_MESSAGE = "Completed successfully";

		private enum ResultCodes
		{
			Success = 0,
			Exception = 1,
			Cancelled = 1223
		}

		private readonly ILoggerFactory _loggerFactory;

		public TestsRunner(ILoggerFactory loggerFactory)
		{
			_loggerFactory = loggerFactory;
		}

		public async Task<int> RunAsync()
		{
			// -----------------------------------------------------------------------------
			// Do you want to proxy requests through Fiddler? Can be useful for debugging.
			var useFiddler = true;
			// -----------------------------------------------------------------------------

			// Configure ZoomNet client
			var apiKey = Environment.GetEnvironmentVariable("ZOOM_APIKEY");
			var apiSecret = Environment.GetEnvironmentVariable("ZOOM_APISECRET");
			var userId = Environment.GetEnvironmentVariable("ZOOM_USERID");
			var proxy = useFiddler ? new WebProxy("http://localhost:8888") : null;
			var client = new Client(apiKey, apiSecret, proxy, null, _loggerFactory.CreateLogger<Client>());

			// Configure Console
			var source = new CancellationTokenSource();
			Console.CancelKeyPress += (s, e) =>
			{
				e.Cancel = true;
				source.Cancel();
			};

			// Ensure the Console is tall enough and centered on the screen
			Console.WindowHeight = Math.Min(60, Console.LargestWindowHeight);
			Utils.CenterConsole();

			// These are the integration tests that we will execute
			var integrationTests = new Type[]
			{
				typeof(Meetings),
			};

			// Execute the async tests in parallel (with max degree of parallelism)
			var results = await integrationTests.ForEachAsync(
				async testType =>
				{
					var log = new StringWriter();

					try
					{
						var integrationTest = (IIntegrationTest)Activator.CreateInstance(testType);
						await integrationTest.RunAsync(userId, client, log, source.Token).ConfigureAwait(false);
						return (TestName: testType.Name, ResultCode: ResultCodes.Success, Message: SUCCESSFUL_TEST_MESSAGE);
					}
					catch (OperationCanceledException)
					{
						await log.WriteLineAsync($"-----> TASK CANCELLED").ConfigureAwait(false);
						return (TestName: testType.Name, ResultCode: ResultCodes.Cancelled, Message: "Task cancelled");
					}
					catch (Exception e)
					{
						var exceptionMessage = e.GetBaseException().Message;
						await log.WriteLineAsync($"-----> AN EXCEPTION OCCURRED: {exceptionMessage}").ConfigureAwait(false);
						return (TestName: testType.Name, ResultCode: ResultCodes.Exception, Message: exceptionMessage);
					}
					finally
					{
						lock (Console.Out)
						{
							Console.Out.WriteLine(log.ToString());
						}
					}
				}, MAX_ZOOM_API_CONCURRENCY)
			.ConfigureAwait(false);

			// Display summary
			var summary = new StringWriter();
			await summary.WriteLineAsync("\n\n**************************************************").ConfigureAwait(false);
			await summary.WriteLineAsync("******************** SUMMARY *********************").ConfigureAwait(false);
			await summary.WriteLineAsync("**************************************************").ConfigureAwait(false);

			foreach (var (TestName, ResultCode, Message) in results.OrderBy(r => r.TestName).ToArray())
			{
				var name = TestName.Length <= TEST_NAME_MAX_LENGTH ? TestName : TestName.Substring(0, TEST_NAME_MAX_LENGTH - 3) + "...";
				await summary.WriteLineAsync($"{name.PadRight(TEST_NAME_MAX_LENGTH, ' ')} : {Message}").ConfigureAwait(false);
			}

			await summary.WriteLineAsync("**************************************************").ConfigureAwait(false);
			await Console.Out.WriteLineAsync(summary.ToString()).ConfigureAwait(false);

			// Prompt user to press a key in order to allow reading the log in the console
			var promptLog = new StringWriter();
			await promptLog.WriteLineAsync("\n\n**************************************************").ConfigureAwait(false);
			await promptLog.WriteLineAsync("Press any key to exit").ConfigureAwait(false);
			Utils.Prompt(promptLog.ToString());

			// Return code indicating success/failure
			var resultCode = (int)ResultCodes.Success;
			if (results.Any(result => result.ResultCode != ResultCodes.Success))
			{
				if (results.Any(result => result.ResultCode == ResultCodes.Exception)) resultCode = (int)ResultCodes.Exception;
				else if (results.Any(result => result.ResultCode == ResultCodes.Cancelled)) resultCode = (int)ResultCodes.Cancelled;
				else resultCode = (int)results.First(result => result.ResultCode != ResultCodes.Success).ResultCode;
			}

			return await Task.FromResult(resultCode);
		}
	}
}
