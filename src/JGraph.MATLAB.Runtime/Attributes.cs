namespace JGraph.MATLAB.Runtime
{
    /// <summary>
    /// Marks a client assembly whose runtime runs without the JVM, as R2025b's attribute of the same
    /// name does. The JGraph Runtime has no JVM, so the mark changes nothing (stage K9).
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class NojvmAttribute : Attribute
    {
        /// <summary>Marks the assembly.</summary>
        public NojvmAttribute(bool isJvmDisabled)
        {
            IsJvmDisabled = isJvmDisabled;
        }

        /// <summary>Whether the JVM is disabled.</summary>
        public bool IsJvmDisabled { get; }
    }

    /// <summary>
    /// Marks a client assembly whose runtime runs in a process of its own, as R2025b's attribute does.
    /// The JGraph Runtime runs in the client's process; the mark is read and changes nothing (stage K9).
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class OutOfProcessAttribute : Attribute
    {
        /// <summary>Marks the assembly.</summary>
        public OutOfProcessAttribute(bool isOutOfProcess = true)
        {
            IsOutOfProcess = isOutOfProcess;
        }

        /// <summary>Whether the runtime runs out of process.</summary>
        public bool IsOutOfProcess { get; }
    }

    /// <summary>A runtime option (<c>-nojvm</c>, <c>-logfile,f</c> and the rest of mcc's <c>-R</c>) a client assembly asks for.</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public class RuntimeOptionAttribute : Attribute
    {
        /// <summary>Asks for <paramref name="option"/>.</summary>
        public RuntimeOptionAttribute(string option)
        {
            Option = option;
        }

        /// <summary>The option.</summary>
        public string Option { get; }
    }
}

namespace JGraph.MATLAB.Types
{
    /// <summary>A .NET class or struct that stands for the MATLAB class <see cref="Name"/>: what the strongly typed interface generates for a classdef.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
    public class MATLABClassAttribute : Attribute
    {
        /// <summary>Names the MATLAB class.</summary>
        public MATLABClassAttribute(string name)
        {
            Name = name;
        }

        /// <summary>The name of the class in MATLAB.</summary>
        public string Name { get; }
    }

    /// <summary>A .NET enum whose members stand for the members of the MATLAB enumeration <see cref="Name"/>, by name.</summary>
    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = false, Inherited = true)]
    public class MATLABEnumAttribute : Attribute
    {
        /// <summary>Names the MATLAB enumeration.</summary>
        public MATLABEnumAttribute(string name)
        {
            Name = name;
        }

        /// <summary>The name of the enumeration in MATLAB.</summary>
        public string Name { get; }
    }
}
