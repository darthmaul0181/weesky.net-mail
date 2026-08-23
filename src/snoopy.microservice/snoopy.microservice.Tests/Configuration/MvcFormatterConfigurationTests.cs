using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Reflection;
using weesky.Snoopy.Microservice.Configuration;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Configuration;

// Controller tests never run an output formatter, so nothing caught CreateFolder answering
// text/plain. This runs the production configuration itself.
public sealed class MvcFormatterConfigurationTests
{
    [Fact]
    public void ConfigureFormatters_DropsTheStringFormatterSoStringsSerialiseAsJson()
    {
        var options = new MvcOptions();
        options.OutputFormatters.Add(new StringOutputFormatter());
        options.OutputFormatters.Add(new HttpNoContentOutputFormatter());

        MvcFormatterConfiguration.ConfigureFormatters(options);

        Assert.DoesNotContain(options.OutputFormatters, f => f is StringOutputFormatter);
        Assert.Contains(options.OutputFormatters, f => f is HttpNoContentOutputFormatter);
    }

    /// <summary>
    /// <c>DavCredentialsViewTests</c> serialises with the real policy object, so it proves the
    /// policy is right — not that the host still applies it. Deleting the one
    /// <c>AddJsonOptions</c> line in Program.cs would leave that suite green while every response
    /// in the product lost <c>WhenWritingNull</c>. Program.cs has no test assembly of its own, so
    /// the call site is read off its compiled IL.
    /// </summary>
    [Fact]
    public void TheHost_StillHandsConfigureJsonToAddJsonOptions()
    {
        var configureJson = typeof(MvcFormatterConfiguration)
            .GetMethod(nameof(MvcFormatterConfiguration.ConfigureJson))!;

        Assert.Contains(typeof(Program).Assembly.GetTypes().SelectMany(t => t.GetMethods(AnyMember)),
            method => Names(method).Contains(configureJson)
                      && Names(method).Any(m => m.Name == "AddJsonOptions"));
    }

    private const BindingFlags AnyMember = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>Every method this one names: <c>call</c>, <c>callvirt</c>, and the <c>ldftn</c> a
    /// method-group conversion emits — which is how a static method reaches AddJsonOptions.</summary>
    private static List<MethodBase> Names(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        var named = new List<MethodBase>();
        if (il is null) return named;

        for (var i = 0; i + 5 < il.Length; i++)
        {
            var isCall = il[i] is 0x28 or 0x6F;
            var isLdftn = il[i] == 0xFE && il[i + 1] == 0x06;
            if (!isCall && !isLdftn) continue;

            try
            {
                var target = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + (isLdftn ? 2 : 1)));
                if (target is not null) named.Add(target);
            }
            catch (ArgumentException)
            {
                // The scan reads operand bytes as opcodes too; a token resolving to nothing is one
                // of those, not a call.
            }
        }

        return named;
    }
}
