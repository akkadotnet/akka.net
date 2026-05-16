using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using MessagePack;
using MessagePack.Formatters;

namespace Akka.Serialization.MessagePack
{
    /// <summary>
    /// A Custom <see cref="MessagePackSerializerOptions"/>
    /// That allows us to, among other things,
    /// Apply custom type filtering logic.
    /// </summary>
    public class MessagePackTypeFilteringOptions : MessagePackSerializerOptions
    {
        private MessagePackTypeFilteringOptions(IFormatterResolver resolver) : base(resolver)
        {
        }

        public MessagePackTypeFilteringOptions(MessagePackSerializerOptions copyFrom) : base(copyFrom)
        {
        }
        
        private static ReadOnlyCollection<string> unsafeTypesDenySet = 
            new ReadOnlyCollection<string>(new[]
            {
                "System.Security.Claims.ClaimsIdentity",
                "System.Windows.Forms.AxHost.State",
                "System.Windows.Data.ObjectDataProvider",
                "System.Management.Automation.PSObject",
                "System.Web.Security.RolePrincipal",
                "System.IdentityModel.Tokens.SessionSecurityToken",
                "SessionViewStateHistoryItem",
                "TextFormattingRunProperties",
                "ToolboxItemContainer",
                "System.Security.Principal.WindowsClaimsIdentity",
                "System.Security.Principal.WindowsIdentity",
                "System.Security.Principal.WindowsPrincipal",
                "System.CodeDom.Compiler.TempFileCollection",
                "System.IO.FileSystemInfo",
                "System.Activities.Presentation.WorkflowDesigner",
                "System.Windows.ResourceDictionary",
                "System.Windows.Forms.BindingSource",
                "Microsoft.Exchange.Management.SystemManager.WinForms.ExchangeSettingsProvider",
                "System.Diagnostics.Process",
                "System.Management.IWbemClassObjectFreeThreaded"
            });
        
        public static bool UnsafeInheritanceCheck(Type type)
        {
            if (type.IsValueType)
                return false;
            var currentBase = type.BaseType;
            while (currentBase != null)
            {
                if (unsafeTypesDenySet.Any(r => currentBase.FullName?.Contains(r) ?? false))
                    return true;
                currentBase = currentBase.BaseType;
            }

            return false;
        }

        private readonly ConcurrentDictionary<Type, bool> allowedCache =
            new ConcurrentDictionary<Type, bool>();
        public override void ThrowIfDeserializingTypeIsDisallowed(Type type)
        {
            if (!allowedCache.TryGetValue(type, out var allowed))
            {
                allowed = setAllowed(type);   
            }
            if (!allowed)
            {
                throw new MessagePackSerializationException("Deserialization attempted to create the type " + type.FullName + " which is not allowed.");
            }
            //base.ThrowIfDeserializingTypeIsDisallowed(type);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool setAllowed(Type type)
        {
            bool allowed;
            allowed =
                unsafeTypesDenySet.Any(r => type.FullName.Contains(r)) ==
                false && UnsafeInheritanceCheck(type) == false;
            allowedCache[type] = allowed;
            return allowed;
        }
    }
}