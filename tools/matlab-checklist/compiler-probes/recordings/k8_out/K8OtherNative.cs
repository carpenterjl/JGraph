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

namespace K8Native
{

  /// <summary>
  /// The K8Other class provides a CLS compliant, Object (native) interface to the MATLAB
  /// functions contained in the files:
  /// <newpara></newpara>
  /// <TEMP>\claude\D--EE-Projects-JGraph-JGraph\0bce8303-f80f-
  /// 4010-b035-d2edbb754408\scratchpad\k8rec\b\src\k8other.m
  /// </summary>
  /// <remarks>
  /// @Version 1.0
  /// </remarks>
  public class K8Other : IDisposable
  {
    #region Constructors

    /// <summary internal= "true">
    /// The static constructor instantiates and initializes the MATLAB Runtime instance.
    /// </summary>
    static K8Other()
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
    /// Constructs a new instance of the K8Other class.
    /// </summary>
    public K8Other()
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
    ~K8Other()
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
    /// Provides a single output, 0-input Objectinterface to the k8other MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8OTHER The second class's one function: twice its argument.
    /// </remarks>
    /// <returns>An Object containing the first output argument.</returns>
    ///
    public Object k8other()
    {
      return mcr.EvaluateFunction("k8other", new Object[]{});
    }


    /// <summary>
    /// Provides a single output, 1-input Objectinterface to the k8other MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8OTHER The second class's one function: twice its argument.
    /// </remarks>
    /// <param name="a">Input argument #1</param>
    /// <returns>An Object containing the first output argument.</returns>
    ///
    public Object k8other(Object a)
    {
      return mcr.EvaluateFunction("k8other", a);
    }


    /// <summary>
    /// Provides the standard 0-input Object interface to the k8other MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8OTHER The second class's one function: twice its argument.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8other(int numArgsOut)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8other", new Object[]{});
    }


    /// <summary>
    /// Provides the standard 1-input Object interface to the k8other MATLAB function.
    /// </summary>
    /// <remarks>
    /// M-Documentation:
    /// K8OTHER The second class's one function: twice its argument.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return.</param>
    /// <param name="a">Input argument #1</param>
    /// <returns>An Array of length "numArgsOut" containing the output
    /// arguments.</returns>
    ///
    public Object[] k8other(int numArgsOut, Object a)
    {
      return mcr.EvaluateFunction(numArgsOut, "k8other", a);
    }


    /// <summary>
    /// Provides an interface for the k8other function in which the input and output
    /// arguments are specified as an array of Objects.
    /// </summary>
    /// <remarks>
    /// This method will allocate and return by reference the output argument
    /// array.<newpara></newpara>
    /// M-Documentation:
    /// K8OTHER The second class's one function: twice its argument.
    /// </remarks>
    /// <param name="numArgsOut">The number of output arguments to return</param>
    /// <param name= "argsOut">Array of Object output arguments</param>
    /// <param name= "argsIn">Array of Object input arguments</param>
    /// <param name= "varArgsIn">Array of Object representing variable input
    /// arguments</param>
    ///
    [MATLABSignature("k8other", 1, 1, 0)]
    protected void k8other(int numArgsOut, ref Object[] argsOut, Object[] argsIn, params Object[] varArgsIn)
    {
        mcr.EvaluateFunctionForTypeSafeCall("k8other", numArgsOut, ref argsOut, argsIn, varArgsIn);
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
