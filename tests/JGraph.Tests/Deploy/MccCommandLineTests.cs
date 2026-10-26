using JGraph.Deploy;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// mcc's command line (compiler and SDK plan, stage K6) against what R2025b's mcc answered: stage K0's
/// refusals (<c>recordings/build_side.out.txt</c>) and stage K6's builds (<c>recordings/k6_build.out.txt</c>).
/// </summary>
public sealed class MccCommandLineTests
{
    private static MccException Refused(params string[] args) => Assert.Throws<MccException>(() => MccCommandLine.Parse(args));

    [Fact]
    public void TheRefusalsAreR2025bs()
    {
        // build_side: each refusal's identifier and text, newlines where k0_flat wrote " / ".
        Assert.Equal("Compiler:compiler:COM_ERR_OPTION_F_MUST_BE_ALONE", Refused("-F", "x.prj", "-d", "out").Identifier);
        MccException mex = Refused("-x", "k0_args.m");
        Assert.Equal("Compiler:compiler:COM_ERR_NO_MORE_MEX", mex.Identifier);
        Assert.StartsWith("Error: -x is no longer supported. The MATLAB Compiler no longer generates \n  MEX files", mex.Message);
        Assert.Equal("Compiler:compiler:COM_ERR_MISSING_WRAPPER", Refused("-L", "C", "k0_args.m").Identifier);
        Assert.Equal("Compiler:compiler:COM_ERR_MISSING_WRAPPER", Refused("-t", "k0_args.m").Identifier);
        Assert.Equal(
            "Unable to determine application type, since no wrapper function was specified. \nPlease use the -W switch or specify application type via -m or -l. \nType 'mcc -?' for further assistance.",
            Refused("-t", "k0_args.m").Message);
        Assert.Equal("Error: No source files were specified (-? for help).", Refused("-m").Message);
        Assert.Equal("Error: 'bogus' is not a valid -W option.", Refused("-W", "bogus:x", "k0_args.m").Message);
        Assert.Equal("Compiler:compiler:COM_ERR_WRAPPER_TARGET_MISMATCH", Refused("-W", "main:k0_args", "-T", "link:lib", "k0_args.m").Identifier);
        Assert.Equal("Compiler:compiler:COM_ERR_WRAPPER_TARGET_MISMATCH", Refused("-W", "WinMain:k6_wl", "-T", "link:lib", "k6_hello.m").Identifier);
        Assert.Equal("Error: \"link:nothing\" is not a valid -T option argument.", Refused("-T", "link:nothing", "k0_args.m").Message);
        Assert.Equal("Error: Unrecognized option: -Q.", Refused("-Q", "k0_args.m").Message);
        Assert.Equal(
            "Version string \"1.2.3.4.5\" is invalid. Use at most four integers (0 to 65534), separated by \".\", to specify the version.",
            Refused("-W", "main:k0_args,version=1.2.3.4.5", "-T", "link:exe", "k0_args.m").Message);
        Assert.Equal("Compiler:compiler:COM_ERR_INVALID_VERSION", Refused("-W", "main:k0_args,version=70000", "-T", "link:exe", "k0_args.m").Identifier);

        // k6_build: names R2025b refuses for a program.
        foreach (string name in new[] { "k6 spaced", "k6/sub", "k6_ext.exe" })
        {
            MccException refused = Refused("-m", "k6_hello.m", "-o", name);
            Assert.Equal("Compiler:compiler:COM_ERR_ILLEGAL_OUTPUT_NAME", refused.Identifier);
            Assert.Equal(
                $"Illegal output name: '{name}' \n (standalone and shared library target output names must begin with a letter or '_' and contain only alpha-numeric characters or '_')",
                refused.Message);
        }
    }

    [Fact]
    public void TheLastTargetSwitchWins()
    {
        // build_side's mcc.e.and.m built a windowed program; k6_build's e_and_W_main a console one.
        Assert.Equal(MccTarget.Windowed, MccCommandLine.Parse(["-m", "-e", "k0_args.m"]).Target);
        Assert.Equal(MccTarget.Console, MccCommandLine.Parse(["-e", "-W", "main:k6_ew", "k6_hello.m"]).Target);
        Assert.Equal(MccTarget.Windowed, MccCommandLine.Parse(["-W", "winmain:k6_wm,version=4.5.6.7", "-T", "link:exe", "k6_hello.m"]).Target);
    }

    [Fact]
    public void TheWrapperNamesTheComponentAndItsVersion()
    {
        MccCommandLine line = MccCommandLine.Parse(["-W", "main:k6_ver,version=2.3", "-T", "link:exe", "k6_hello.m"]);
        Assert.Equal("k6_ver", line.ComponentName);
        Assert.Equal("2.3.0.0", line.Version.ToString());
        Assert.Null(line.OutputName); // the program is still named for the main (k6_build: m_version wrote k6_hello.exe)
        Assert.Equal(RuntimeDelivery.Bundled, MccCommandLine.Parse(["-W", "main:app,runtime=bundled", "app.m"]).RuntimeDelivery);
    }

    [Fact]
    public void TheStandaloneSwitchesAreRead()
    {
        MccCommandLine line = MccCommandLine.Parse(
        [
            "-m", "app.m", "-o", "named", "-n", "-C", "-r", "icon.ico", "-h", "help.txt", "-a", "data.mat", "-a", "assets",
            "-d", "out", "-v", "-X", "-Z", "autodetect", "-R", "-startmsg,hi", "-j", "extra.m",
        ]);
        Assert.Equal(["app.m", "extra.m"], line.Files);
        Assert.Equal("named", line.OutputName);
        Assert.True(line.NumericArguments && line.SeparateArchive && line.Verbose && line.NoDataFiles && line.PCode);
        Assert.Equal("icon.ico", line.IconFile);
        Assert.Equal("help.txt", line.HelpFile);
        Assert.Equal(["data.mat", "assets"], line.Additional);
        Assert.Equal("out", line.OutputFolder);
        Assert.Equal(["-startmsg,hi"], line.RuntimeOptions);
        Assert.True(MccCommandLine.Parse(["-?"]).ShowHelp);
    }

    [Fact]
    public void WhatIsNotBuiltIsSaidSo()
    {
        Assert.Equal("JGraph:mcc:optionNotBuilt", Refused("-m", "-s", "app.m").Identifier);
        Assert.Equal("JGraph:mcc:optionNotBuilt", Refused("-m", "-k", "key", "app.m").Identifier);
        Assert.Equal("JGraph:mcc:targetNotBuilt", Refused("-W", "python:mypkg", "app.m").Identifier);
        Assert.Equal("JGraph:mcc:targetOutOfScope", Refused("-W", "cpplib:mylib,legacy", "app.m").Identifier); // decision KD7
        Assert.Equal("Compiler:compiler:REQ_UNEXPECTED_ERROR", Refused("-m", "-Z", "Arduino", "app.m").Identifier); // k7_switches: Z_bogus
    }

    [Theory]
    [InlineData("1", "1.0.0.0")]
    [InlineData("3.4", "3.4.0.0")]
    [InlineData("2.3.4.5", "2.3.4.5")]
    [InlineData("65534.0.0.1", "65534.0.0.1")]
    [InlineData("1.2.3.4.5", null)]
    [InlineData("70000", null)]
    [InlineData("abc", null)]
    [InlineData("1..2", null)]
    [InlineData("", null)]
    [InlineData("-1", null)]
    public void VersionsAreReadAsR2025bReadsThem(string text, string? expected) =>
        Assert.Equal(expected, ProgramVersion.Parse(text)?.ToString());
}
