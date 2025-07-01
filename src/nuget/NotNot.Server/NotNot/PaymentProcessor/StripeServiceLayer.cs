using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace NotNot.PaymentProcessor;

/// <summary>
/// wrapper over https://github.com/stripe/stripe-dotnet 
/// api docs here: https://stripe.com/docs/api?lang=dotnet
/// </summary>
/// <param name="_logger"></param>
/// <param name="_optionsBinder"></param>
public class StripeServiceLayer(ILogger<StripeServiceLayer> _logger, IOptions<StripeServiceLayer.Options> _optionsBinder) : ISingletonService, IAutoInitialize
{
	public class Options
	{
		public string? StripeSecret { get; set; }
	}
	public StripeClient StripeClient { get; protected set; }

	public CustomerService CustomerService { get; protected set; }
	public SubscriptionService SubscriptionService { get; protected set; }
	public ProductService ProductService { get; protected set; }

	/// <summary>
	/// https://stripe.com/docs/payments/payment-intents
	/// </summary>
	public PaymentIntentService PaymentIntentService { get; protected set; }
	public async ValueTask AutoInitialize(IServiceProvider services, CancellationToken ct)
	{
		//any needed initialization can go here
		var stripeApiKey = _optionsBinder.Value.StripeSecret;
		_logger._EzErrorThrow(stripeApiKey is not null, "stripeApiKey is null.  you need to set via builder.Services.Configure<StripeServiceLayer.Options>()");
		StripeClient = new StripeClient(stripeApiKey);
		CustomerService = new CustomerService(StripeClient);

		SubscriptionService = new SubscriptionService(StripeClient);
		ProductService = new ProductService(StripeClient);

		PaymentIntentService = new PaymentIntentService(StripeClient);
	}

	public async Task<List<Product>> GetProducts(CancellationToken ct)
	{
		var values = await ProductService.ListAsync(cancellationToken: ct);
		_logger._EzError(values.HasMore is false, "we assume all records are returned, this is not true so NEED to rework this logic");
		return values.ToList();
	}
}
