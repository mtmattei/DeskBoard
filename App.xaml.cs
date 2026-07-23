using System;
using System.Linq;
using System.Windows;

namespace DeskBoard;

public partial class App : Application
{
    /// <summary>--board starts directly in Board mode (used for verification/screenshots).</summary>
    public static bool StartInBoardMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartInBoardMode = e.Args.Any(a =>
            string.Equals(a, "--board", StringComparison.OrdinalIgnoreCase));
        base.OnStartup(e);
    }
}
