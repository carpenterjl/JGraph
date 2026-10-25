/*
* MATLAB Compiler: 25.2 (R2025b)
* Date: <DATE>
* Arguments:
* "-B""macro_default""-W""dotnet:K8,api=mwarray,K8Class,5.0,Private,local,version=1.0.0.0"
* "-T""link:lib""-d""<TEMP>\claude\D--EE-Projects-JGraph-JGraph
* \<UUID>\scratchpad\k8rec\b\dn\K8""-v""-Z""autodetect""clas
* s{K8Class:<TEMP>\claude\D--EE-Projects-JGraph-JGraph\0bce8303
* -f80f-4010-b035-d2edbb754408\scratchpad\k8rec\b\src\k8args.m,<HOME>\AppData\Loca
* l\Temp\claude\D--EE-Projects-JGraph-JGraph\<UUID>\scratchp
* ad\k8rec\b\src\k8echo.m,<TEMP>\claude\D--EE-Projects-JGraph-J
* Graph\<UUID>\scratchpad\k8rec\b\src\k8err.m,<HOME>
* \AppData\Local\Temp\claude\D--EE-Projects-JGraph-JGraph\0bce8303-f80f-4010-b035-d2edbb75
* 4408\scratchpad\k8rec\b\src\k8make.m,<TEMP>\claude\D--EE-Proj
* ects-JGraph-JGraph\<UUID>\scratchpad\k8rec\b\src\k8quiet.m
* ,<TEMP>\claude\D--EE-Projects-JGraph-JGraph\0bce8303-f80f-401
* 0-b035-d2edbb754408\scratchpad\k8rec\b\src\k8show.m,<TEMP>\cl
* aude\D--EE-Projects-JGraph-JGraph\<UUID>\scratchpad\k8rec\
* b\src\k8vout.m}""class{K8Other:<TEMP>\claude\D--EE-Projects-J
* Graph-JGraph\<UUID>\scratchpad\k8rec\b\src\k8other.m}"
*/
using System;
using System.Reflection;
using System.IO;
using MathWorks.MATLAB.NET.Arrays;
using MathWorks.MATLAB.NET.Utility;

#if SHARED
[assembly: System.Reflection.AssemblyKeyFile(@"")]
#endif

namespace K8Native
{

  /// <summary>
  /// The K8Class class provides a CLS compliant, Object (native) interface to the MATLAB
  /// functions contained in the files:
  /// <newpara></newpara>
  /// <TEMP>\claude\D--EE-Projects-JGraph-JGraph\0bce8303-f80f-
  /// 4010-b035-d2edbb754408\scratchpad\k8rec\b\src\k8args.m
  /// <newpara></newpara>
  /// <TEMP>\claude\D--EE-Projects-JGraph-JGraph\0bce8303-f80f-
  /// 4010-b035-d2edbb754408\scratchpad\k8rec\b\src\k8echo.m
  /// <newpara></newpara>
  /// <TEMP>\claude\D--EE-Projects-JGraph-JGraph\0bce8303-f80f-
  /// 4010-b035-d2edbb754408\scratchpad\k8rec\b\src\k8err.m
  /// <newpara></newpara>
  /// <TEMP>\claude\D--EE-Projects-JGraph-JGraph\0bce8303-f80f-
  /// 4010-b035-d2edbb754408\scratchpad\k8rec\b\src\k8make.m
  /// <newpara></newpara>
  /// <TEMP>\claude\D--EE-Projects-JGraph-JGraph\0bce8303-f80f-
  /// 4010-b035-d2edbb754408\scratchpad\k8rec\b\src\k8quiet.m
  /// <newpara></newpara>
  /// <TEMP>\claude\D--EE-Projects-JGraph-JGraph\0bce8303-f80f-
  /// 4010-b035-d2edbb754408\scratchpad\k8rec\b\src\k8show.m
  /// <newpara></newpara>
  /// <TEMP>\claude\D--EE-Projects-JGraph-JGraph\0bce8303-f80f-
  /// 4010-b035-d2edbb754408\scratchpad\k8rec\b\src\k8vout.m
  /// </summary>
  /// <remarks>
  /// @Version 1.0
  /// </remarks>
  public class K8Class : IDisposable
  {
    #region Constructors

    /// <summary internal= "true">
    /// The static constructor instantiates and initializes the MATLAB Runtime instance.
    /// </summary>
    static K8Class()
    {
      if (MWMCR.MCRAppInitialized)
      {
        try
        {
          System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();

          string ctfFilePath= assembly.Location;

		  int lastDelimiter = ctfFilePath.LastIndexOf(@"/");

	      if (lastDelimiter == -1)
		  {
		    lastDelimiter = ctfFilePath.LastIndexOf(@"\");
		  }

          ctfFilePath= ctfFilePath.Remove(lastDelimiter, (ctfFilePath.Length - lastDelimiter));

          string ctfFileName = "K8.ctf";

          Stream embeddedCtfStream = null;

          String[] resourceStrings = assembly.GetManifestResourceNames();

          foreach (String name in resourceStrings)
          {
            if (name.Contains(ctfFileName))
            {
              embeddedCtfStream = assembly.GetManifestResourceStream(name);
              break;
            }
          }
          mcr= new MWMCR("",
                         ctfFilePath, embeddedCtfStream, true);
        }
        catch(Exception ex)
        {
          ex_ = new Exception("MWArray assembly failed to be initialized", ex);
        }
      }
      else
      {
        ex_ = new ApplicationException("MWArray assembly could not be initialized");
      }
    }


    /// <summary>
    /// Constructs a new instance of the K8Class class.
    /// </summary>
    public K8Class()
    {
      if(ex_ != null)
      {
        throw ex_;
      }
    }


    #endregion Constructors

    #region Finalize

    /// <summary internal= "true">
    /// Class destructor called by the CLR garbage collector.
    /// </summary>
    ~K8Class()
    {
      Dispose(false);
    }


    /// <summary>
    /// Frees the native resources associated with this object
    /// </summary>
    public void Dispose()
    {
      Dispose(true);

      GC.SuppressFinalize(this);
    }


    /// <summary internal= "true">
    /// Internal dispose function
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
      if (!disposed)
      {
        disposed= true;

        if (disposing)
        {
          // Free managed resources;
        }

        // Free native resources
      }
    }


    #endregion Finalize

    #region Methods

    /// <summary>
    /// Provides a single output, 0-input Objectinterface to the k8args MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ARGS Count the arguments given; the second output is the last argument.
    /// </remarks>
    /// <returns>An Object containing the first output argument.</returns>
    ///
    public Object k8args()
    {
      return mcr.EvaluateFunction("k8args", new Object[]{});
    }


    /// <summary>
    /// Provides a single output, 1-input Objectinterface to the k8args MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ARGS Count the arguments given; the second output is the last argument.
    /// </remarks>
    /// <param name="first">Input argument #1</param>
    /// <returns>An Object containing the first output argument.</returns>
    ///
    public Object k8args(Object first)
    {
      return mcr.EvaluateFunction("k8args", first);
    }


    /// <summary>
    /// Provides a single output, 2-input Objectinterface to the k8args MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ARGS Count the arguments given; the second output is the last argument.
    /// </remarks>
    /// <param name="first">Input argument #1</param>
    /// <param name="second">Input argument #2</param>
    /// <returns>An Object containing the first output argument.</returns>
    ///
    public Object k8args(Object first, Object second)
    {
      return mcr.EvaluateFunction("k8args", first, second);
    }


    /// <summary>
    /// Provides a single output, 3-input Objectinterface to the k8args MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ARGS Count the arguments given; the second output is the last argument.
    /// </remarks>
    /// <param name="first">Input argument #1</param>
    /// <param name="second">Input argument #2</param>
    /// <param name="varargin">Array of Objects representing the input arguments 3
    /// through varargin.length+2</param>
    /// <returns>An Object containing the first output argument.</returns>
    ///
    public Object k8args(Object first, Object second, params Object[] varargin)
    {
      Object[] argsIn= {first, second, varargin};

      return mcr.EvaluateFunction("k8args", argsIn);
    }


    /// <summary>
    /// Provides the standard 0-input Object interface to the k8args MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ARGS Count the arguments given; the second output is the last argument.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8args(int numArgsOut)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8args", new Object[]{});
    }


    /// <summary>
    /// Provides the standard 1-input Object interface to the k8args MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ARGS Count the arguments given; the second output is the last argument.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <param name="first">Input argument #1</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8args(int numArgsOut, Object first)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8args", first);
    }


    /// <summary>
    /// Provides the standard 2-input Object interface to the k8args MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ARGS Count the arguments given; the second output is the last argument.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <param name="first">Input argument #1</param>
    /// <param name="second">Input argument #2</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8args(int numArgsOut, Object first, Object second)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8args", first, second);
    }


    /// <summary>
    /// Provides the standard 3-input Object interface to the k8args MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ARGS Count the arguments given; the second output is the last argument.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <param name="first">Input argument #1</param>
    /// <param name="second">Input argument #2</param>
    /// <param name="varargin">Array of Objects representing the input arguments 3
    /// through varargin.length+2</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8args(int numArgsOut, Object first, Object second, params Object[] 
                     varargin)
    {
      Object[] argsIn= {first, second, varargin};

      return mcr.EvaluateFunction(numArgsOut, "k8args", argsIn);
    }


    /// <summary>
    /// Provides an interface for the k8args function in which the input and output
    /// arguments are specified as an array of Objects.
    /// </summary>
    /// <remarks>
    /// This method will allocate and return by reference the output argument
    /// array.<newpara></newpara>
    /// M-Documentation:
    /// K8ARGS Count the arguments given; the second output is the last argument.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return</param>
    /// <param name= "argsOut">Array of Object output arguments</param>
    /// <param name= "argsIn">Array of Object input arguments</param>
    /// <param name= "varArgsIn">Array of Object representing variable input
    /// arguments</param>
    ///
    [MATLABSignature("k8args", 2, 2, 1)]
    protected void k8args(int numArgsOut, ref Object[] argsOut, Object[] argsIn, params Object[] varArgsIn)
    {
        mcr.EvaluateFunctionForTypeSafeCall("k8args", numArgsOut, ref argsOut, argsIn, varArgsIn);
    }
    /// <summary>
    /// Provides a single output, 0-input Objectinterface to the k8echo MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ECHO Return the argument and a line saying what it arrived as.
    /// </remarks>
    /// <returns>An Object containing the first output argument.</returns>
    ///
    public Object k8echo()
    {
      return mcr.EvaluateFunction("k8echo", new Object[]{});
    }


    /// <summary>
    /// Provides a single output, 1-input Objectinterface to the k8echo MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ECHO Return the argument and a line saying what it arrived as.
    /// </remarks>
    /// <param name="x">Input argument #1</param>
    /// <returns>An Object containing the first output argument.</returns>
    ///
    public Object k8echo(Object x)
    {
      return mcr.EvaluateFunction("k8echo", x);
    }


    /// <summary>
    /// Provides the standard 0-input Object interface to the k8echo MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ECHO Return the argument and a line saying what it arrived as.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8echo(int numArgsOut)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8echo", new Object[]{});
    }


    /// <summary>
    /// Provides the standard 1-input Object interface to the k8echo MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ECHO Return the argument and a line saying what it arrived as.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <param name="x">Input argument #1</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8echo(int numArgsOut, Object x)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8echo", x);
    }


    /// <summary>
    /// Provides an interface for the k8echo function in which the input and output
    /// arguments are specified as an array of Objects.
    /// </summary>
    /// <remarks>
    /// This method will allocate and return by reference the output argument
    /// array.<newpara></newpara>
    /// M-Documentation:
    /// K8ECHO Return the argument and a line saying what it arrived as.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return</param>
    /// <param name= "argsOut">Array of Object output arguments</param>
    /// <param name= "argsIn">Array of Object input arguments</param>
    /// <param name= "varArgsIn">Array of Object representing variable input
    /// arguments</param>
    ///
    [MATLABSignature("k8echo", 1, 2, 0)]
    protected void k8echo(int numArgsOut, ref Object[] argsOut, Object[] argsIn, params Object[] varArgsIn)
    {
        mcr.EvaluateFunctionForTypeSafeCall("k8echo", numArgsOut, ref argsOut, argsIn, varArgsIn);
    }
    /// <summary>
    /// Provides a void output, 0-input Objectinterface to the k8err MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ERR Raise the error asked for, for the client to see how it arrives.
    /// </remarks>
    ///
    public void k8err()
    {
      mcr.EvaluateFunction(0, "k8err", new Object[]{});
    }


    /// <summary>
    /// Provides a void output, 1-input Objectinterface to the k8err MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ERR Raise the error asked for, for the client to see how it arrives.
    /// </remarks>
    /// <param name="kind">Input argument #1</param>
    ///
    public void k8err(Object kind)
    {
      mcr.EvaluateFunction(0, "k8err", kind);
    }


    /// <summary>
    /// Provides the standard 0-input Object interface to the k8err MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ERR Raise the error asked for, for the client to see how it arrives.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8err(int numArgsOut)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8err", new Object[]{});
    }


    /// <summary>
    /// Provides the standard 1-input Object interface to the k8err MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8ERR Raise the error asked for, for the client to see how it arrives.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <param name="kind">Input argument #1</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8err(int numArgsOut, Object kind)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8err", kind);
    }


    /// <summary>
    /// Provides an interface for the k8err function in which the input and output
    /// arguments are specified as an array of Objects.
    /// </summary>
    /// <remarks>
    /// This method will allocate and return by reference the output argument
    /// array.<newpara></newpara>
    /// M-Documentation:
    /// K8ERR Raise the error asked for, for the client to see how it arrives.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return</param>
    /// <param name= "argsOut">Array of Object output arguments</param>
    /// <param name= "argsIn">Array of Object input arguments</param>
    /// <param name= "varArgsIn">Array of Object representing variable input
    /// arguments</param>
    ///
    [MATLABSignature("k8err", 1, 0, 0)]
    protected void k8err(int numArgsOut, ref Object[] argsOut, Object[] argsIn, params Object[] varArgsIn)
    {
        mcr.EvaluateFunctionForTypeSafeCall("k8err", numArgsOut, ref argsOut, argsIn, varArgsIn);
    }
    /// <summary>
    /// Provides a single output, 0-input Objectinterface to the k8make MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8MAKE Return a value of the kind asked for, for the client to read back.
    /// </remarks>
    /// <returns>An Object containing the first output argument.</returns>
    ///
    public Object k8make()
    {
      return mcr.EvaluateFunction("k8make", new Object[]{});
    }


    /// <summary>
    /// Provides a single output, 1-input Objectinterface to the k8make MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8MAKE Return a value of the kind asked for, for the client to read back.
    /// </remarks>
    /// <param name="kind">Input argument #1</param>
    /// <returns>An Object containing the first output argument.</returns>
    ///
    public Object k8make(Object kind)
    {
      return mcr.EvaluateFunction("k8make", kind);
    }


    /// <summary>
    /// Provides the standard 0-input Object interface to the k8make MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8MAKE Return a value of the kind asked for, for the client to read back.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8make(int numArgsOut)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8make", new Object[]{});
    }


    /// <summary>
    /// Provides the standard 1-input Object interface to the k8make MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8MAKE Return a value of the kind asked for, for the client to read back.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <param name="kind">Input argument #1</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8make(int numArgsOut, Object kind)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8make", kind);
    }


    /// <summary>
    /// Provides an interface for the k8make function in which the input and output
    /// arguments are specified as an array of Objects.
    /// </summary>
    /// <remarks>
    /// This method will allocate and return by reference the output argument
    /// array.<newpara></newpara>
    /// M-Documentation:
    /// K8MAKE Return a value of the kind asked for, for the client to read back.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return</param>
    /// <param name= "argsOut">Array of Object output arguments</param>
    /// <param name= "argsIn">Array of Object input arguments</param>
    /// <param name= "varArgsIn">Array of Object representing variable input
    /// arguments</param>
    ///
    [MATLABSignature("k8make", 1, 1, 0)]
    protected void k8make(int numArgsOut, ref Object[] argsOut, Object[] argsIn, params Object[] varArgsIn)
    {
        mcr.EvaluateFunctionForTypeSafeCall("k8make", numArgsOut, ref argsOut, argsIn, varArgsIn);
    }
    /// <summary>
    /// Provides a single output, 0-input Objectinterface to the k8quiet MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8QUIET Turn the beep off and say what it was.
    /// </remarks>
    /// <returns>An Object containing the first output argument.</returns>
    ///
    public Object k8quiet()
    {
      return mcr.EvaluateFunction("k8quiet", new Object[]{});
    }


    /// <summary>
    /// Provides the standard 0-input Object interface to the k8quiet MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8QUIET Turn the beep off and say what it was.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8quiet(int numArgsOut)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8quiet", new Object[]{});
    }


    /// <summary>
    /// Provides an interface for the k8quiet function in which the input and output
    /// arguments are specified as an array of Objects.
    /// </summary>
    /// <remarks>
    /// This method will allocate and return by reference the output argument
    /// array.<newpara></newpara>
    /// M-Documentation:
    /// K8QUIET Turn the beep off and say what it was.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return</param>
    /// <param name= "argsOut">Array of Object output arguments</param>
    /// <param name= "argsIn">Array of Object input arguments</param>
    /// <param name= "varArgsIn">Array of Object representing variable input
    /// arguments</param>
    ///
    [MATLABSignature("k8quiet", 0, 1, 0)]
    protected void k8quiet(int numArgsOut, ref Object[] argsOut, Object[] argsIn, params Object[] varArgsIn)
    {
        mcr.EvaluateFunctionForTypeSafeCall("k8quiet", numArgsOut, ref argsOut, argsIn, varArgsIn);
    }
    /// <summary>
    /// Provides a void output, 0-input Objectinterface to the k8show MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8SHOW Display the argument; no outputs.
    /// </remarks>
    ///
    public void k8show()
    {
      mcr.EvaluateFunction(0, "k8show", new Object[]{});
    }


    /// <summary>
    /// Provides a void output, 1-input Objectinterface to the k8show MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8SHOW Display the argument; no outputs.
    /// </remarks>
    /// <param name="x">Input argument #1</param>
    ///
    public void k8show(Object x)
    {
      mcr.EvaluateFunction(0, "k8show", x);
    }


    /// <summary>
    /// Provides the standard 0-input Object interface to the k8show MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8SHOW Display the argument; no outputs.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8show(int numArgsOut)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8show", new Object[]{});
    }


    /// <summary>
    /// Provides the standard 1-input Object interface to the k8show MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8SHOW Display the argument; no outputs.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <param name="x">Input argument #1</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8show(int numArgsOut, Object x)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8show", x);
    }


    /// <summary>
    /// Provides an interface for the k8show function in which the input and output
    /// arguments are specified as an array of Objects.
    /// </summary>
    /// <remarks>
    /// This method will allocate and return by reference the output argument
    /// array.<newpara></newpara>
    /// M-Documentation:
    /// K8SHOW Display the argument; no outputs.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return</param>
    /// <param name= "argsOut">Array of Object output arguments</param>
    /// <param name= "argsIn">Array of Object input arguments</param>
    /// <param name= "varArgsIn">Array of Object representing variable input
    /// arguments</param>
    ///
    [MATLABSignature("k8show", 1, 0, 0)]
    protected void k8show(int numArgsOut, ref Object[] argsOut, Object[] argsIn, params Object[] varArgsIn)
    {
        mcr.EvaluateFunctionForTypeSafeCall("k8show", numArgsOut, ref argsOut, argsIn, varArgsIn);
    }
    /// <summary>
    /// Provides a void output, 0-input Objectinterface to the k8vout MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8VOUT Answer as many outputs as asked for: output k is k, the last says nargin
    /// and nargout.
    /// </remarks>
    ///
    public void k8vout()
    {
      mcr.EvaluateFunction(0, "k8vout", new Object[]{});
    }


    /// <summary>
    /// Provides a void output, 1-input Objectinterface to the k8vout MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8VOUT Answer as many outputs as asked for: output k is k, the last says nargin
    /// and nargout.
    /// </remarks>
    /// <param name="varargin">Array of Objects representing the input arguments 1
    /// through varargin.length</param>
    ///
    public void k8vout(params Object[] varargin)
    {
      mcr.EvaluateFunction(0, "k8vout", varargin);
    }


    /// <summary>
    /// Provides the standard 0-input Object interface to the k8vout MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8VOUT Answer as many outputs as asked for: output k is k, the last says nargin
    /// and nargout.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8vout(int numArgsOut)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8vout", new Object[]{});
    }


    /// <summary>
    /// Provides the standard 1-input Object interface to the k8vout MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8VOUT Answer as many outputs as asked for: output k is k, the last says nargin
    /// and nargout.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <param name="varargin">Array of Objects representing the input arguments 1
    /// through varargin.length</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8vout(int numArgsOut, params Object[] varargin)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8vout", varargin);
    }


    /// <summary>
    /// Provides an interface for the k8vout function in which the input and output
    /// arguments are specified as an array of Objects.
    /// </summary>
    /// <remarks>
    /// This method will allocate and return by reference the output argument
    /// array.<newpara></newpara>
    /// M-Documentation:
    /// K8VOUT Answer as many outputs as asked for: output k is k, the last says nargin
    /// and nargout.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return</param>
    /// <param name= "argsOut">Array of Object output arguments</param>
    /// <param name= "argsIn">Array of Object input arguments</param>
    /// <param name= "varArgsIn">Array of Object representing variable input
    /// arguments</param>
    ///
    [MATLABSignature("k8vout", 0, 1, 1)]
    protected void k8vout(int numArgsOut, ref Object[] argsOut, Object[] argsIn, params Object[] varArgsIn)
    {
        mcr.EvaluateFunctionForTypeSafeCall("k8vout", numArgsOut, ref argsOut, argsIn, varArgsIn);
    }

    /// <summary>
    /// This method will cause a MATLAB figure window to behave as a modal dialog box.
    /// The method will not return until all the figure windows associated with this
    /// component have been closed.
    /// </summary>
    /// <remarks>
    /// An application should only call this method when required to keep the
    /// MATLAB figure window from disappearing.  Other techniques, such as calling
    /// Console.ReadLine() from the application should be considered where
    /// possible.</remarks>
    ///
    public void WaitForFiguresToDie()
    {
      mcr.WaitForFiguresToDie();
    }



    #endregion Methods

    #region Class Members

    private static MWMCR mcr= null;

    private static Exception ex_= null;

    private bool disposed= false;

    #endregion Class Members
  }
}
