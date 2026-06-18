using System;
using System.Net;
using NotNot;
using NotNot.Diagnostics;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// External-boundary contract pin for the SPEC-009 error-explanation registry: the resolution order
/// (registered explainer → <c>Problem.Detail</c> → <c>Problem.Title</c> → generic) and each key axis
/// (exception Type / category / Status), plus the Exception/Maybe normalization entry points.
/// </summary>
public class ErrorExplanationRegistryTests
{
	private static Problem MakeProblem(
		string category = "Unknown",
		HttpStatusCode status = HttpStatusCode.InternalServerError,
		string? detail = null,
		string? title = null)
		=> new() { category = category, Status = status, Detail = detail, Title = title };

	// ---- key axis: exception Type (via Problem.Ex) -------------------------------------------------

	[Fact]
	public void ExceptionAxis_MatchesByRuntimeType()
	{
		var reg = new ErrorExplanationRegistry();
		reg.RegisterByException<InvalidOperationException>(_ => "by-ex");

		var problem = Problem.FromEx(new InvalidOperationException("boom"));

		Assert.Equal("by-ex", reg.Explain(problem));
	}

	[Fact]
	public void ExceptionAxis_WalksBaseChain_WhenOnlyBaseRegistered()
	{
		var reg = new ErrorExplanationRegistry();
		reg.RegisterByException<Exception>(_ => "base-ex");

		var problem = Problem.FromEx(new InvalidOperationException("boom"));

		Assert.Equal("base-ex", reg.Explain(problem));
	}

	[Fact]
	public void ExceptionAxis_MostDerivedWins_OverBase()
	{
		var reg = new ErrorExplanationRegistry();
		reg.RegisterByException<Exception>(_ => "base-ex")
		   .RegisterByException<InvalidOperationException>(_ => "derived-ex");

		var problem = Problem.FromEx(new InvalidOperationException("boom"));

		Assert.Equal("derived-ex", reg.Explain(problem));
	}

	// ---- key axis: category ------------------------------------------------------------------------

	[Fact]
	public void CategoryAxis_Matches()
	{
		var reg = new ErrorExplanationRegistry();
		reg.RegisterByCategory(Problem.CategoryNames.DbIo, _ => "db-text");

		var problem = MakeProblem(category: Problem.CategoryNames.DbIo, detail: "raw");

		Assert.Equal("db-text", reg.Explain(problem));
	}

	// ---- key axis: Status --------------------------------------------------------------------------

	[Fact]
	public void StatusAxis_Matches()
	{
		var reg = new ErrorExplanationRegistry();
		reg.RegisterByStatus(HttpStatusCode.NotFound, _ => "not-found-text");

		var problem = MakeProblem(category: "Unknown", status: HttpStatusCode.NotFound, detail: "raw");

		Assert.Equal("not-found-text", reg.Explain(problem));
	}

	// ---- resolution order: exception > category > status -------------------------------------------

	[Fact]
	public void ResolutionOrder_ExceptionBeatsCategoryAndStatus()
	{
		var reg = new ErrorExplanationRegistry();
		reg.RegisterByException<InvalidOperationException>(_ => "by-ex")
		   .RegisterByCategory(Problem.CategoryNames.Unknown, _ => "by-cat")
		   .RegisterByStatus(HttpStatusCode.InternalServerError, _ => "by-status");

		// Problem.FromEx → category Unknown, Status 500, Ex set.
		var problem = Problem.FromEx(new InvalidOperationException("boom"));

		Assert.Equal("by-ex", reg.Explain(problem));
	}

	[Fact]
	public void ResolutionOrder_CategoryBeatsStatus_WhenNoException()
	{
		var reg = new ErrorExplanationRegistry();
		reg.RegisterByCategory(Problem.CategoryNames.Unknown, _ => "by-cat")
		   .RegisterByStatus(HttpStatusCode.InternalServerError, _ => "by-status");

		var problem = MakeProblem(category: Problem.CategoryNames.Unknown, status: HttpStatusCode.InternalServerError, detail: "d");

		Assert.Equal("by-cat", reg.Explain(problem));
	}

	[Fact]
	public void ResolutionOrder_StatusUsed_WhenNoExceptionOrCategoryMatch()
	{
		var reg = new ErrorExplanationRegistry();
		reg.RegisterByStatus(HttpStatusCode.InternalServerError, _ => "by-status");

		var problem = MakeProblem(category: Problem.CategoryNames.Unknown, status: HttpStatusCode.InternalServerError, detail: "d");

		Assert.Equal("by-status", reg.Explain(problem));
	}

	// ---- fallback chain: Detail → Title → generic --------------------------------------------------

	[Fact]
	public void Fallback_UsesDetail_WhenNoRegisteredExplainer()
	{
		var reg = new ErrorExplanationRegistry();

		var problem = MakeProblem(detail: "the-detail", title: "the-title");

		Assert.Equal("the-detail", reg.Explain(problem));
	}

	[Fact]
	public void Fallback_UsesTitle_WhenDetailBlank()
	{
		var reg = new ErrorExplanationRegistry();

		var problem = MakeProblem(detail: null, title: "the-title");

		Assert.Equal("the-title", reg.Explain(problem));
	}

	[Fact]
	public void Fallback_UsesGeneric_WhenDetailAndTitleBlank()
	{
		var reg = new ErrorExplanationRegistry();

		var problem = MakeProblem(detail: null, title: null);

		Assert.Equal(ErrorExplanationRegistry.GenericFallback, reg.Explain(problem));
	}

	[Fact]
	public void RegisteredExplainer_WinsOverDetailFallback()
	{
		var reg = new ErrorExplanationRegistry();
		reg.RegisterByCategory(Problem.CategoryNames.Validation, _ => "friendly");

		var problem = MakeProblem(category: Problem.CategoryNames.Validation, detail: "technical-detail");

		Assert.Equal("friendly", reg.Explain(problem));
	}

	// ---- normalization entry points ----------------------------------------------------------------

	[Fact]
	public void ExplainException_NormalizesViaFromEx()
	{
		var reg = new ErrorExplanationRegistry();
		reg.RegisterByException<TimeoutException>(_ => "timeout-text");

		Assert.Equal("timeout-text", reg.Explain(new TimeoutException()));
	}

	[Fact]
	public void ExplainFailedMaybe_NormalizesToCarriedProblem()
	{
		var reg = new ErrorExplanationRegistry();
		reg.RegisterByCategory(Problem.CategoryNames.DbIo, _ => "db-text");

		Maybe<int> failed = MakeProblem(category: Problem.CategoryNames.DbIo, detail: "raw");

		Assert.Equal("db-text", reg.Explain(failed));
	}

	[Fact]
	public void ExplainSuccessfulMaybe_Throws()
	{
		var reg = new ErrorExplanationRegistry();

		Maybe<int> ok = Maybe<int>.Success(5);

		Assert.Throws<ArgumentException>(() => reg.Explain(ok));
	}

	// ---- last-registered-wins per key --------------------------------------------------------------

	[Fact]
	public void Registration_LastWins_PerCategoryKey()
	{
		var reg = new ErrorExplanationRegistry();
		reg.RegisterByCategory(Problem.CategoryNames.NetIo, _ => "first")
		   .RegisterByCategory(Problem.CategoryNames.NetIo, _ => "second");

		var problem = MakeProblem(category: Problem.CategoryNames.NetIo);

		Assert.Equal("second", reg.Explain(problem));
	}
}
