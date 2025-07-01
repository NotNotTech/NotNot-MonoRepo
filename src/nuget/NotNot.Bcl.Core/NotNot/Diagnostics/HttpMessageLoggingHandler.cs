namespace NotNot.Diagnostics;

/// <summary>
///    from
///    https://learn.microsoft.com/en-us/aspnet/web-api/overview/advanced/http-message-handlers#custom-message-handlers
///    example showing how to intercept requests
/// </summary>
public class HttpMessageLoggingHandler : DelegatingHandler
{
	//public HttpMessageLoggingHandler()
	//{
	//   if (InnerHandler == null)
	//   {
	//      InnerHandler = new HttpClientHandler();
	//   }
	//}
	private AsyncLazy<ILogger> lazyLogger = __.GetLoggerLazy<HttpMessageHandler>();


	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken ct)
	{
		var logger = await lazyLogger;


		try
		{
			if (logger._IfInfo())
			{
				//log details of the request
				//Debug.WriteLine("Process request DEBUG WRITELINE!");
				logger._EzInfo("Sending HttpRequestMessage", request.Method, request.RequestUri, request);
				//Log.Information("Sending HttpRequestMessage: {@Method} {@URI} {@request}", request.Method, request.RequestUri, request);
			}

			// Call the inner handler.
			var response = await base.SendAsync(request, ct);

			string contentString;
			try
			{
				contentString = await response.Content.ReadAsStringAsync(ct);
			}
			catch (Exception ex)
			{
				contentString = $"[ERROR calling response.Content.ReadAsStringAsync(): {ex._ToUserFriendlyString()}";
			}

			if (logger._IfInfo())
			{
				//var requestInfo = new { Url = "https://myurl.com/data", Payload = 12 };
				//var req1 = (response, contentString);
				//var req2 = new { response, contentString };

				//Log.Debug("Received HttpResponseMessage: {@StatusCode} {@response} {@content}",
				//   response.StatusCode, response, new StringHelper { Val = contentString });

				logger._EzInfo("Sending HttpRequestMessage",
					new { statusCode = response.StatusCode, requestUri = request.RequestUri, response, contentString });
			}

			return response;
		}
		catch (Exception ex)
		{
			logger._EzError(ex);
			//Log.Error(ex, "Error logging http request/response");
			throw;
		}
	}
}
