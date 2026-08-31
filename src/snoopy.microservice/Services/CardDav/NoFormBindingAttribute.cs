using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// Keeps MVC's form value providers away from a controller that reads its own body. Every value
/// provider is built BEFORE any parameter is bound, whatever its binding source, and the form
/// one reads the request body as soon as the content type is a form's — which is what curl sends
/// for <c>--data</c> when no <c>Content-Type</c> is given. The action then reads an empty body,
/// and a valid card answers <c>403 valid-address-data</c>. <c>[FromRoute]</c> does not prevent it.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class NoFormBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        context.ValueProviderFactories.RemoveType<FormValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<FormFileValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
