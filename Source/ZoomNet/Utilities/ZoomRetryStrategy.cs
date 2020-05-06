using Pathoschild.Http.Client.Retry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace ZoomNet.Utilities
{
	/// <summary>
	/// Implements IRetryConfig with back off based on a wait time derived from the
	/// "Retry-After" response header. The value in this header contains the date
	/// and time when the next attempt can take place.
	/// </summary>
	/// <seealso cref="Pathoschild.Http.Client.Retry.IRetryConfig" />
	internal class ZoomRetryStrategy : IRetryConfig
	{
		#region FIELDS

		private const int DEFAULT_MAX_RETRIES = 4;
		private const HttpStatusCode TOO_MANY_REQUESTS = (HttpStatusCode)429;
		private static readonly TimeSpan DEFAULT_DELAY = TimeSpan.FromSeconds(1);

		private readonly ISystemClock _systemClock;

		#endregion

		#region PROPERTIES

		/// <summary>Gets the maximum number of times to retry a request before failing.</summary>
		public int MaxRetries { get; }

		#endregion

		#region CTOR

		/// <summary>
		/// Initializes a new instance of the <see cref="ZoomRetryStrategy" /> class.
		/// </summary>
		public ZoomRetryStrategy()
			: this(DEFAULT_MAX_RETRIES, null)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="ZoomRetryStrategy" /> class.
		/// </summary>
		/// <param name="maxAttempts">The maximum attempts.</param>
		public ZoomRetryStrategy(int maxAttempts)
			: this(maxAttempts, null)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="ZoomRetryStrategy" /> class.
		/// </summary>
		/// <param name="maxAttempts">The maximum attempts.</param>
		/// <param name="systemClock">The system clock. This is for unit testing only.</param>
		internal ZoomRetryStrategy(int maxAttempts, ISystemClock systemClock = null)
		{
			MaxRetries = maxAttempts;
			_systemClock = systemClock ?? SystemClock.Instance;
		}

		#endregion

		#region PUBLIC METHODS

		/// <summary>
		/// Checks if we should retry an operation.
		/// </summary>
		/// <param name="response">The Http response of the previous request.</param>
		/// <returns>
		///   <c>true</c> if another attempt should be made; otherwise, <c>false</c>.
		/// </returns>
		public bool ShouldRetry(HttpResponseMessage response)
		{
			return response != null && response.StatusCode == TOO_MANY_REQUESTS;
		}

		/// <summary>
		/// Gets a TimeSpan value which defines how long to wait before trying again after an unsuccessful attempt.
		/// </summary>
		/// <param name="attempt">The number of attempts carried out so far. That is, after the first attempt (for
		/// the first retry), attempt will be set to 1, after the second attempt it is set to 2, and so on.</param>
		/// <param name="response">The Http response of the previous request.</param>
		/// <returns>
		/// A TimeSpan value which defines how long to wait before the next attempt.
		/// </returns>
		public TimeSpan GetDelay(int attempt, HttpResponseMessage response)
		{
			// Default value in case the 'reset' time is missing from HTTP headers
			var waitTime = DEFAULT_DELAY;

			// Get the 'retry-after' time from the HTTP headers (if present)
			if (response?.Headers != null)
			{
				if (response.Headers.TryGetValues("Retry-After", out IEnumerable<string> values))
				{
					if (DateTime.TryParse(values?.FirstOrDefault(), out DateTime nextRetryDateTime))
					{
						waitTime = nextRetryDateTime.Subtract(_systemClock.UtcNow);
					}
				}
			}

			// Make sure the wait time is valid
			if (waitTime.TotalMilliseconds < 0) waitTime = DEFAULT_DELAY;

			// Totally arbitrary. Make sure we don't wait more than a 'reasonable' amount of time
			if (waitTime.TotalSeconds > 5) waitTime = TimeSpan.FromSeconds(5);

			return waitTime;
		}

		#endregion
	}
}
