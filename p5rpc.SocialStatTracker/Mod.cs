using p5rpc.SocialStatTracker.Configuration;
using p5rpc.SocialStatTracker.Template;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Memory.Sigscan.Definitions.Structs;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using System.Diagnostics;
using Reloaded.Memory;
using static Reloaded.Hooks.Definitions.X64.FunctionAttribute;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

using static p5rpc.SocialStatTracker.Utils;

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
        private IAsmHook _lvlUpGetStatHook;
        private IAsmHook _getStatHook;
        private IReverseWrapper<AddPointsNeededFunc> _addPointsNeededReverseWrapper;
        private short* _socialStatPoints;

        private int* _currentSocialStat;
        private int* _currentStatLevel;

        private nuint _downwardMoveConst;

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks;
            _logger = context.Logger;
            _owner = context.Owner;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;

            Initialise(_logger, _configuration, _modLoader);
            
            var memory = Memory.Instance;
            _currentSocialStat = (int*)memory.Allocate(4).Address;
            _currentStatLevel = (int*)memory.Allocate(4).Address;

            var addPointsCall = _hooks.Utilities.GetAbsoluteCallMnemonics(AddPointsNeeded, out _addPointsNeededReverseWrapper);
            
            SigScan("4C 8D 35 ?? ?? ?? ?? 0F B7 FD", "Social stat points", address =>
            {
                _socialStatPoints = (short*)GetGlobalAddress((nuint)address + 3);
                LogDebug($"Social stat points start at 0x{(nuint)_socialStatPoints:X}");
            });
            
            SigScan("49 6B C0 64", "Social Stat Level", address =>
            {
                if (_socialStatPoints == null) return;
                
                string[] function =
                {
                    "use64",
                    $"mov [qword {(nuint)_currentSocialStat}], r8d",
                    $"mov [qword {(nuint)_currentStatLevel}], eax",
                };
                
                _getStatHook = _hooks.CreateAsmHook(function, address, AsmHookBehaviour.ExecuteFirst).Activate();
            });


            SigScan("E8 ?? ?? ?? ?? 4C 8B 0D ?? ?? ?? ?? 49 FF C4", "Social stat text", address =>
            {
                if (_socialStatPoints == null) return;
            
                string[] function =
                {
                    "use64",
                    $"push r8 \npush rdx \npush rcx \npush r9 \npush r11",
                    $"{PushXmm(0)}\n{PushXmm(4)}\n{PushXmm(1)}",
                    "mov rcx, r8", // Current stat name string
                    $"mov rdx, {(nuint)_currentSocialStat}", // Stat id
                    "mov rdx, [rdx]",
                    $"mov r8, {(nuint)_currentStatLevel}", // Stat level
                    "mov r8, [r8]",
                    "sub rsp, 40",
                    addPointsCall,
                    "add rsp, 40",
                    $"{PopXmm(1)}\n{PopXmm(4)}\n{PopXmm(0)}",
                    "pop r11 \npop r9 \npop rcx \npop rdx\npop r8",
                    "mov [rsp + 0x30], rax"
                 };
                _textHook = _hooks.CreateAsmHook(function, address, AsmHookBehaviour.ExecuteFirst).Activate();
            });
            
            SigScan("46 0F B7 5C ?? ?? 46 0F B7 54 ?? ??", "Social Stat Gain Stat Level", address =>
            {
                if (_socialStatPoints == null) return;
                
                string[] function =
                {
                    "use64",
                    $"mov [qword {(nuint)_currentSocialStat}], r14d",
                    $"mov [qword {(nuint)_currentStatLevel}], r10d",
                };
                
                _lvlUpGetStatHook = _hooks.CreateAsmHook(function, address, AsmHookBehaviour.ExecuteAfter).Activate();
            });

            SigScan("E8 ?? ?? ?? ?? 4C 8B 05 ?? ?? ?? ?? 49 FF C5", "Social stat gain", address =>
            {
                if (_socialStatPoints == null) return;

                string[] function =
                {
                    "use64",
                    $"push r8 \npush rdx \npush rcx \npush r9 \npush r11",
                    "mov r8, r10",
                    $"{PushXmm(0)}\n{PushXmm(4)}\n{PushXmm(5)}\n{PushXmm(1)}",
                    "mov rcx, r8", // Current stat name string
                    $"mov rdx, {(nuint)_currentSocialStat}", // Stat id
                    "mov rdx, [rdx]",
                    $"mov r8, {(nuint)_currentStatLevel}", // Stat level
                    "mov r8, [r8]",
                    "sub rsp, 40",
                    addPointsCall,
                    "add rsp, 40",
                    $"{PopXmm(1)}\n{PopXmm(5)}\n{PopXmm(4)}\n{PopXmm(0)}",
                    "pop r11 \npop r9 \npop rcx \npop rdx",
                    "mov [rsp + 0x38], rax",
                    "pop r8",
                 };
                _textLvlUpHook = _hooks.CreateAsmHook(function, address, AsmHookBehaviour.ExecuteFirst).Activate();
            });

        }
        
        private string AddPointsNeeded(string currentString, int socialStat, int level)
        {
            level--; // We want a 0 based level, this is 1 based
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