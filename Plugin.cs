using System;
using System.Runtime.InteropServices;
using Dalamud.Game.Internal.Gui;
using Dalamud.Hooking;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace DalamudMenu
{
    public unsafe class Plugin : IDalamudPlugin
    {
        private DalamudPluginInterface pluginInterface;

        private delegate void AgentHudOpenSystemMenuProtoype(void* thisPtr, AtkValue* atkValueArgs, uint menuSize);

        private Hook<AgentHudOpenSystemMenuProtoype> hookAgentHudOpenSystemMenu;

        private delegate void AtkValueChangeType(AtkValue* thisPtr, ValueType type);

        private AtkValueChangeType atkValueChangeType;

        private delegate void AtkValueSetString(AtkValue* thisPtr, byte* bytes);

        private AtkValueSetString atkValueSetString;

        private delegate void UiModuleRequestMainCommand(void* thisPtr, int commandId);

        private Hook<UiModuleRequestMainCommand> hookUiModuleRequestMainCommand;

        public string Name => "Dalamud Menu Test";

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;

            var openSystemMenuAddress = this.pluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? 32 C0 4C 8B AC 24 ?? ?? ?? ?? 48 8B 8D ?? ?? ?? ??");

            this.hookAgentHudOpenSystemMenu = new Hook<AgentHudOpenSystemMenuProtoype>(openSystemMenuAddress,
                new AgentHudOpenSystemMenuProtoype(AgentHudOpenSystemMenuDetour), this);
            this.hookAgentHudOpenSystemMenu.Enable();

            var atkValueChangeTypeAddress =
                this.pluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? 45 84 F6 48 8D 4C 24 ??");
            this.atkValueChangeType =
                Marshal.GetDelegateForFunctionPointer<AtkValueChangeType>(atkValueChangeTypeAddress);

            var atkValueSetStringAddress =
                this.pluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? 41 03 ED");
            this.atkValueSetString = Marshal.GetDelegateForFunctionPointer<AtkValueSetString>(atkValueSetStringAddress);

            var uiModuleRequestMainCommmandAddress = this.pluginInterface.TargetModuleScanner.ScanText(
                "40 53 56 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 48 8B 01 8B DA 48 8B F1 FF 90 ?? ?? ?? ??");
            this.hookUiModuleRequestMainCommand = new Hook<UiModuleRequestMainCommand>(
                uiModuleRequestMainCommmandAddress, new UiModuleRequestMainCommand(UiModuleRequestMainCommandDetour),
                this);
            this.hookUiModuleRequestMainCommand.Enable();


        }

        private void AgentHudOpenSystemMenuDetour(void* thisPtr, AtkValue* atkValueArgs, uint menuSize)
        {
            // the max size (hardcoded) is 0xE/15, but the system menu currently uses 0xC/12
            // this is a just in case that doesnt really matter
            // see if we can add 2 entries
            if (menuSize >= 0xD)
            {
                hookAgentHudOpenSystemMenu.Original(thisPtr, atkValueArgs, menuSize);
                return;
            }

            // atkValueArgs is actually an array of AtkValues used as args. all their UI code works like this.
            // in this case, menu size is stored in atkValueArgs[4], and the next 15 slots are the MainCommand
            // the 15 slots after that, if they exist, are the entry names, but they are otherwise pulled from MainCommand EXD
            // reference the original function for more details :)

            // step 1) move all the current menu items down so we can put Dalamud at the top like it deserves
            atkValueChangeType(&atkValueArgs[menuSize + 5], ValueType.Int); // currently this value has no type, set it to int
            atkValueChangeType(&atkValueArgs[menuSize + 5 + 1], ValueType.Int);

            for (uint i = menuSize+2; i > 1; i--)
            {
                var curEntry = &atkValueArgs[i + 5 - 2];
                var nextEntry = &atkValueArgs[i + 5];

                nextEntry->Int = curEntry->Int;
            }

            // step 2) set our new entries to dummy commands
            var firstEntry = &atkValueArgs[5];
            firstEntry->Int = 69420;
            var secondEntry = &atkValueArgs[6];
            secondEntry->Int = 69421;

            // step 3) create strings for them
            // since the game first checks for strings in the AtkValue argument before pulling them from the exd, if we create strings we dont have to worry
            // about hooking the exd reader, thank god
            var firstStringEntry = &atkValueArgs[5 + 15];
            atkValueChangeType(firstStringEntry, ValueType.String);
            var secondStringEntry = &atkValueArgs[6 + 15];
            atkValueChangeType(secondStringEntry, ValueType.String);

            // do this the most terrible way possible since im lazy
            var bytes = stackalloc byte[17];
            Marshal.Copy(System.Text.Encoding.ASCII.GetBytes("Dalamud Settings"), 0, new IntPtr(bytes), 16);
            bytes[16] = 0x0;

            atkValueSetString(firstStringEntry, bytes); // this allocs the string properly using the game's allocators and copies it, so we dont have to worry about memory fuckups

            var bytes2 = stackalloc byte[16];
            Marshal.Copy(System.Text.Encoding.ASCII.GetBytes("Dalamud Plugins"), 0, new IntPtr(bytes2), 15);
            bytes2[15] = 0x0;

            atkValueSetString(secondStringEntry, bytes2);

            // open menu with new size
            var sizeEntry = &atkValueArgs[4];
            sizeEntry->UInt = menuSize + 2;

            this.hookAgentHudOpenSystemMenu.Original(thisPtr, atkValueArgs, menuSize + 2);
        }

        private void UiModuleRequestMainCommandDetour(void* thisPtr, int commandId)
        {
            if (commandId == 69420)
            {
                this.pluginInterface.CommandManager.ProcessCommand("/xlsettings");
            }
            else if (commandId == 69421)
            {
                this.pluginInterface.CommandManager.ProcessCommand("/xlplugins");
            }
            else
            {
                this.hookUiModuleRequestMainCommand.Original(thisPtr, commandId);
            }
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            this.hookAgentHudOpenSystemMenu.Disable();
            this.hookAgentHudOpenSystemMenu.Dispose();

            this.hookUiModuleRequestMainCommand.Disable();
            this.hookUiModuleRequestMainCommand.Dispose();

            this.pluginInterface.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
