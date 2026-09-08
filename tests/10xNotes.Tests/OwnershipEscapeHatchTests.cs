using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using _10xnotes.Auth;
using _10xnotes.Data;
using _10xnotes.Profiles;

namespace _10xNotes.Tests;

/// <summary>
/// Pins who is allowed to name a user other than the signed-in one.
/// </summary>
/// <remarks>
/// Two members take a bare <see cref="Guid"/> and decide whose rows a caller sees:
/// <c>UserScopedDbContextFactory.CreateForAsync</c>, which mints a fully-functional context scoped
/// to any id, and <c>ProfileService.GetDisplayNameAtSignInAsync</c>, which reads through it. They
/// exist for exactly one moment — the sign-in POST, after GoTrue has verified the credentials but
/// before the cookie exists, where the current-user accessor necessarily reports nobody.
/// <para>
/// Everywhere else that id must come from the accessor. Nothing in the type system says so: both
/// members are <c>public</c>, and a future page could pass
/// <c>[SupplyParameterFromQuery] Guid userId</c> to either and read another account's data with no
/// test failing. This file is that test.
/// </para>
/// <para>
/// It is the third executable ownership invariant, beside <c>DataAccessBoundaryTests</c> (nothing
/// injects a <c>DbContext</c> or <c>IDbContextFactory&lt;&gt;</c>) and
/// <c>AccountDeletionServiceTests.The_signature_offers_no_way_to_name_another_user</c> (the delete
/// exposes no id parameter). The doc comments on both members say the same thing in prose; a
/// comment cannot fail a build.
/// </para>
/// <para>
/// If this test fails, the new caller is the thing to justify — not this list. Widening it is a
/// decision about the ownership model, so make it deliberately.
/// </para>
/// </remarks>
public sealed class OwnershipEscapeHatchTests
{
    [Fact]
    public void Only_the_factory_itself_and_the_sign_in_read_may_scope_a_context_to_a_given_id()
    {
        AssertCallersAre(
            typeof(UserScopedDbContextFactory).GetMethod(nameof(UserScopedDbContextFactory.CreateForAsync))!,
            [
                // The ordinary path: takes the id from the current-user accessor and delegates.
                "UserScopedDbContextFactory.CreateAsync",
                // The sign-in read, whose id is GoTrue's verified response.
                "ProfileService.GetDisplayNameAtSignInAsync"
            ]);
    }

    [Fact]
    public void Only_the_login_page_may_read_a_display_name_by_id()
    {
        AssertCallersAre(
            typeof(ProfileService).GetMethod(nameof(ProfileService.GetDisplayNameAtSignInAsync))!,
            ["Login.ReadDisplayNameAsync"]);
    }

    private static void AssertCallersAre(MethodInfo target, string[] allowed)
    {
        var actual = CallersOf(target).OrderBy(name => name, StringComparer.Ordinal).ToList();
        var unexpected = actual.Except(allowed, StringComparer.Ordinal).ToList();

        Assert.True(
            unexpected.Count == 0,
            $"{target.DeclaringType!.Name}.{target.Name} lets a caller name a user other than the "
            + "signed-in one, so its caller set is part of the ownership model rather than an "
            + "implementation detail. Unexpected callers: "
            + string.Join(", ", unexpected)
            + ". If the new caller is legitimate, add it to the allow-list in this test and say why "
            + "its id cannot come from user input.");

        // Also fail if an expected caller disappeared, so the allow-list cannot rot into a list of
        // names that no longer mean anything.
        var missing = allowed.Except(actual, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0,
            $"The allow-list for {target.DeclaringType!.Name}.{target.Name} names callers that no "
            + "longer exist: " + string.Join(", ", missing)
            + ". Remove them, or the list stops describing the code.");
    }

    /// <summary>
    /// Every method in the application assembly whose IL contains a call to <paramref name="target"/>,
    /// as <c>Type.Method</c>.
    /// </summary>
    private static IEnumerable<string> CallersOf(MethodInfo target)
    {
        // Reached through a public type: top-level statements make Program internal.
        var assembly = typeof(AuthCookie).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.Static
                         | BindingFlags.DeclaredOnly))
            {
                if (Calls(method, target))
                {
                    yield return Describe(method);
                }
            }
        }
    }

    /// <summary>
    /// Names the method a reader would recognise, unwrapping the compiler-generated types an
    /// <c>async</c> method body actually lives in.
    /// </summary>
    /// <remarks>
    /// Every caller here is <c>async</c>, so the IL is in a nested <c>&lt;Name&gt;d__N</c> state
    /// machine rather than in the method itself. Without unwrapping, the allow-list would have to
    /// name compiler-generated types, which change with the compiler.
    /// </remarks>
    private static string Describe(MethodBase method)
    {
        var type = method.DeclaringType!;
        var name = method.Name;

        while (type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
               && type.DeclaringType is not null)
        {
            // "<ReadDisplayNameAsync>d__12" -> "ReadDisplayNameAsync"
            var open = type.Name.IndexOf('<');
            var close = type.Name.IndexOf('>');

            if (open == 0 && close > 1)
            {
                name = type.Name[1..close];
            }

            type = type.DeclaringType;
        }

        return $"{type.Name}.{name}";
    }

    private static bool Calls(MethodBase method, MethodInfo target)
    {
        byte[] il;

        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            // Abstract, extern or otherwise bodiless.
            return false;
        }

        foreach (var token in CallTokens(il))
        {
            MethodBase? called;

            try
            {
                called = method.Module.ResolveMethod(
                    token,
                    method.DeclaringType?.GetGenericArguments() ?? [],
                    method.IsGenericMethod ? method.GetGenericArguments() : []);
            }
            catch (Exception exception) when (exception is ArgumentException or BadImageFormatException)
            {
                continue;
            }

            if (called is not null
                && called.Name == target.Name
                && called.DeclaringType == target.DeclaringType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks the IL instruction by instruction, yielding the metadata token of every
    /// <c>call</c> / <c>callvirt</c>.
    /// </summary>
    /// <remarks>
    /// A real walk, not a scan for the opcode byte: operands contain arbitrary bytes, so scanning
    /// would resolve garbage as tokens and could just as easily miss a real call. Operand widths
    /// come from <see cref="OpCodes"/> itself rather than a hand-written table.
    /// </remarks>
    private static IEnumerable<int> CallTokens(byte[] il)
    {
        var offset = 0;

        while (offset < il.Length)
        {
            var code = il[offset];
            offset++;

            short value = code;

            if (code == 0xFE && offset < il.Length)
            {
                value = (short)(0xFE00 | il[offset]);
                offset++;
            }

            if (!OperandSizes.TryGetValue(value, out var operand))
            {
                // Unknown opcode — the walk has lost sync and anything after it is guesswork.
                yield break;
            }

            if (operand == InlineSwitchMarker)
            {
                if (offset + 4 > il.Length)
                {
                    yield break;
                }

                var count = BitConverter.ToInt32(il, offset);
                offset += 4 + (4 * count);
                continue;
            }

            if (value is CallOpcode or CallvirtOpcode && offset + 4 <= il.Length)
            {
                yield return BitConverter.ToInt32(il, offset);
            }

            offset += operand;
        }
    }

    private const short CallOpcode = 0x28;
    private const short CallvirtOpcode = 0x6F;
    private const int InlineSwitchMarker = -1;

    private static readonly Dictionary<short, int> OperandSizes = BuildOperandSizes();

    private static Dictionary<short, int> BuildOperandSizes()
    {
        var sizes = new Dictionary<short, int>();

        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            sizes[opCode.Value] = opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => InlineSwitchMarker,
                _ => 4
            };
        }

        return sizes;
    }
}
