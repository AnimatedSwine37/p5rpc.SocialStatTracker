using p5rpc.SocialStatTracker.Configuration;
using p5rpc.SocialStatTracker.Template;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Memory.Sigscan.Definitions.Structs;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using System.Diagnostics;
using static Reloaded.Hooks.Definitions.X64.FunctionAttribute;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace p5rpc.SocialStatTracker
{
    /// <summary>
    /// Your mod logic goes here.
    /// </summary>
    public unsafe class Mod : ModBase // <= Do not Remove.
    {
        /// <summary>
        /// Provides access to the mod loader API.
        /// </summary>
        private readonly IModLoader _modLoader;

        /// <summary>
        /// Provides access to the Reloaded.Hooks API.
        /// </summary>
        /// <remarks>This is null if you remove dependency on Reloaded.SharedLib.Hooks in your mod.</remarks>
        private readonly IReloadedHooks? _hooks;

        /// <summary>
        /// Provides access to the Reloaded logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Entry point into the mod, instance that created this class.
        /// </summary>
        private readonly IMod _owner;

        /// <summary>
        /// Provides access to this mod's configuration.
        /// </summary>
        private Config _configuration;

        /// <summary>
        /// The configuration of the currently executing mod.
        /// </summary>
        private readonly IModConfig _modConfig;

        private IAsmHook _textHook;
        private IAsmHook _textLvlUpHook;
        private IReverseWrapper<AddPointsNeededFunc> _addPointsNeededReverseWrapper;
        private short* _socialStatPoints;

        private nuint _downwardMoveConst;

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks;
            _logger = context.Logger;
            _owner = context.Owner;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;


            // For more information about this template, please see
            // https://reloaded-project.github.io/Reloaded-II/ModTemplate/

            // If you want to implement e.g. unload support in your mod,
            // and some other neat features, override the methods in ModBase.

            // TODO: Implement some mod logic

            Utils.Initialise(_logger, _configuration);

            string addPointsCall = _hooks.Utilities.GetAbsoluteCallMnemonics(AddPointsNeeded, out _addPointsNeededReverseWrapper);

            var startupScannerController = _modLoader.GetController<IStartupScanner>();
            if (startupScannerController == null || !startupScannerController.TryGetTarget(out var startupScanner))
            {
                Utils.LogError($"[Input Hook] Unable to get controller for Reloaded SigScan Library, aborting initialisation");
                return;
            }

            startupScanner.AddMainModuleScan("4C 8D 35 ?? ?? ?? ?? 0F B7 FD", result =>
            {
                if (!result.Found)
                {
                    Utils.LogError("Unable to find social stat points, mod no worky :(");
                    return;
                }
                _socialStatPoints = (short*)Utils.GetGlobalAddress((nuint)result.Offset + (nuint)Utils.BaseAddress + 3);
                Utils.LogDebug($"Social stat points start at 0x{(nuint)_socialStatPoints:X}");
            });

            startupScanner.AddMainModuleScan("E8 ?? ?? ?? ?? 4C 8B 0D ?? ?? ?? ?? 49 FF C4", result =>
            {
                if (_socialStatPoints == null) return;
                if (!result.Found)
                {
                    Utils.LogError("Unable to find social stat text code, you won't see points in the social stat screen :(");
                    return;
                }
                Utils.LogDebug($"Found social stat text at 0x{result.Offset + Utils.BaseAddress:X}");

                string[] function =
                {
                    "use64",
                    $"push rcx \npush r9 \npush r10 \npush r11",
                    $"{Utils.PushXmm(0)}\n{Utils.PushXmm(4)}\n{Utils.PushXmm(1)}",
                    $"{addPointsCall}",
                    $"{Utils.PopXmm(1)}\n{Utils.PopXmm(4)}\n{Utils.PopXmm(0)}",
                    "pop r11 \npop r10 \npop r9 \npop rcx",
                    "mov [rsp + 0x30], r8"
                 };
                _textHook = _hooks.CreateAsmHook(function, result.Offset + Utils.BaseAddress, AsmHookBehaviour.ExecuteFirst).Activate();
            });

            // TODO work out how to get the levels properly, seems to change when there's a level up and when there isn't :(
            startupScanner.AddMainModuleScan("E8 ?? ?? ?? ?? 4C 8B 05 ?? ?? ?? ?? 49 FF C5", result =>
            {
                if (_socialStatPoints == null) return;
                if (!result.Found)
                {
                    Utils.LogError("Unable to find social stat gain text code, you won't see points in the social stat gain screen :(");
                    return;
                }
                Utils.LogDebug($"Found social stat gain text at 0x{result.Offset + Utils.BaseAddress:X}");

                string[] function =
                {
                    "use64",
                    $"push r8 \npush rax \npush rcx \npush r9 \npush r11",
                    "mov r8, r10",
                    $"{Utils.PushXmm(0)}\n{Utils.PushXmm(4)}\n{Utils.PushXmm(5)}\n{Utils.PushXmm(1)}",
                    "sub rsp, 8", // Align stack
                    $"{addPointsCall}",
                    "add rsp, 8", // Realign stack
                    $"{Utils.PopXmm(1)}\n{Utils.PopXmm(5)}\n{Utils.PopXmm(4)}\n{Utils.PopXmm(0)}",
                    "pop r11 \npop r9 \npop rcx \npop rax",
                    "mov [rsp + 0x38], r8",
                    "pop r8",
                 };
                _textLvlUpHook = _hooks.CreateAsmHook(function, result.Offset + Utils.BaseAddress, AsmHookBehaviour.ExecuteFirst).Activate();
            });

        }

        
        private string AddPointsNeeded(string currentString, int socialStat, int level)
        {
            level = level / 20;
            socialStat /= 0x64;
            short currentPoints = _socialStatPoints[socialStat];
            if (level > 4) level = GetSocialStatLevel(socialStat, currentPoints); // Normal way breaks when a stat levels up :(
            short lastPointsNeeded = _pointsNeeded[socialStat][level];
            if (level == 4)
            {
                int extraPoints = currentPoints - lastPointsNeeded;
                if (extraPoints == 0)
                    return currentString;
                return $"{currentString} +{extraPoints}";
            }
            short pointsNeeded = _pointsNeeded[socialStat][level + 1];
            return $"{currentString} {currentPoints - lastPointsNeeded}/{pointsNeeded - lastPointsNeeded}";
        }

        private short[][] _pointsNeeded =
        {
            new short[]{ 0, 34, 82, 126, 192},
            new short[]{ 0, 6, 52, 92, 132},
            new short[]{ 0, 12, 34, 60, 87},
            new short[]{ 0, 11, 38, 68, 113},
            new short[]{ 0, 14, 44, 91, 136},
        };

        // Ideally we don't do this for speed but when a stat levels up the level becomes 5 (which it isn't really)
        // Could probably find the actual level somewhere in a register or stack but I cba
        private int GetSocialStatLevel(int socialStat, int currentPoints)
        {
            short[] pointsNeeded = _pointsNeeded[socialStat];
            for(int i = 4; i >= 0; i--)
            {
                if (currentPoints >= pointsNeeded[i])
                    return i;
            }
            return 0;
        }

        [Function(new Register[] { Register.r8, Register.rax, Register.rdx }, Register.r8, true)]
        private delegate string AddPointsNeededFunc(string currentString, int socialStat, int level);

        #region Standard Overrides
        public override void ConfigurationUpdated(Config configuration)
        {
            // Apply settings from configuration.
            // ... your code here.
            _configuration = configuration;
            _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying");
        }
        #endregion

        #region For Exports, Serialization etc.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Mod() { }
#pragma warning restore CS8618
        #endregion
    }
}