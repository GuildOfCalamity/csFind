using System;
using System.Diagnostics;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;

[assembly: AssemblyTitle("csfind")]
[assembly: AssemblyDescription("A grep-style file parser utility, don't leave home without it.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("The Guild")]
[assembly: AssemblyProduct("csfind")]
[assembly: AssemblyCopyright("Copyright © The Guild 2025")]
[assembly: AssemblyTrademark("Chamware")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: Guid("76defb49-5aff-44e6-bd53-cc2844caca51")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0-beta")] // a.k.a. "product version"

#region [Uncommon Attributes]
[assembly: AssemblyFlags(AssemblyNameFlags.PublicKey | AssemblyNameFlags.EnableJITcompileOptimizer | AssemblyNameFlags.EnableJITcompileTracking)]

// Wrap non-Exception throws in a System.Runtime.CompilerServices.RuntimeWrappedException.
// Ensures that throwing a non-Exception type (e.g., an integer) still results in a catchable exception object.
// Improves language interoperability and safer error handling.
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]

// Lets you attach arbitrary key/value pairs to your assembly’s metadata.
// Can be read at runtime via reflection GetCustomAttributes<AssemblyMetadataAttribute>().
// Handy for linking CI build numbers, source branches, or proprietary tags.
[assembly: AssemblyMetadata("RepositoryUrl", "https://github.com/GuildOfCalamity/csFind")]
[assembly: AssemblyMetadata("RepositoryType", "git")]

// Turn off JIT optimizations and enable edit-and-continue (use with debug mode only).
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.Default | DebuggableAttribute.DebuggingModes.DisableOptimizations | DebuggableAttribute.DebuggingModes.EnableEditAndContinue)]

// Selects the code-access security model (Level1 ≈ .NET 2.0, Level2 ≈ .NET 4.x).
// SkipVerificationInFullTrust lets fully trusted callers bypass certain checks.
// Essential when migrating legacy CAS code to newer CLR security standards.
[assembly: SecurityRules(SecurityRuleSet.Level1, SkipVerificationInFullTrust = true)]

// Informs the ResourceManager of the assembly’s default culture.
// Skips probing for satellite assemblies when the current UI culture matches this setting, improving lookup performance.
// If omitted, the ResourceManager assumes invariant culture.
[assembly: NeutralResourcesLanguage("en-US")]

// Controls which folders the runtime probes when you P/Invoke unmanaged DLLs.
// Helps prevent "DLL-hijacking" security issues by narrowing the search paths.
// Available starting in .NET Framework 4.6.1 and .NET Core.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
//[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.ApplicationDirectory)]

// Grants permission for partially-trusted (e.g. sandboxed) code to call into your assembly.
// By default, only fully-trusted callers can use strong-named assemblies.
// Use with caution, exposing sensitive APIs to untrusted code can open security holes.
[assembly: AllowPartiallyTrustedCallers]

// Instructs the JIT to relax certain CLR rules (e.g., skip string interning).
// Can improve startup performance in apps heavy on dynamically generated strings.
// Must be used judiciously—interning often saves memory and avoids duplicate strings.
[assembly: CompilationRelaxations(CompilationRelaxations.NoStringInterning)]

// Grants another assembly access to your internal types and members.
// You can include the public key to restrict access only to a strong-named friend assembly.
// Commonly used to enable unit tests to exercise non-public implementation details.
//[assembly: InternalsVisibleTo("MyLibrary.Tests")]

// Enforces language-agnostic rules (e.g. no unsigned types in public signatures).
// The compiler will warn on any public members that break CLS compliance.
//[assembly: CLSCompliant(true)]

// Tells the compiler to reserve space for the strong-name signature without actually signing the assembly.
// Useful in build pipelines where the private key is only available on a signing server.
// Later you call "sn.exe –R YourAssembly.dll mykey.snk" to complete the signature.
//[assembly: AssemblyDelaySign(true)]

// Embed the public key from a .snk file at compile time
//[assembly: AssemblyKeyFile("mykey.snk")]
// Or reference a key in the CSP (Crypto Service Provider) by name
//[assembly: AssemblyKeyName("MyKeyContainerName")]

// Defines a major/minor/build/revision version tuple used by COM clients.
// Can differ from your AssemblyVersion, allowing independent COM compatibility management.
// Helps COM consumers handle version mismatches gracefully.
//[assembly: ComCompatibleVersion(1, 0, 0, 0)]

// Instruct obfuscators to rename private members but keep public API intact
//[assembly: Obfuscation(Feature = "renaming", Exclude = false, ApplyToMembers = true)]

#endregion