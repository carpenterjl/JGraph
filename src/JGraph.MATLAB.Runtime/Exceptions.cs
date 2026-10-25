namespace JGraph.MATLAB.Exceptions;

/// <summary>Base class for exceptions related to MATLAB, as R2025b's MathWorks.MATLAB.Exceptions.MATLABException.</summary>
public class MATLABException : Exception
{
    /// <inheritdoc/>
    public MATLABException()
    {
    }

    /// <inheritdoc/>
    public MATLABException(string message)
        : base(message)
    {
    }

    /// <inheritdoc/>
    public MATLABException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>An error that occurred during the execution of a MATLAB function.</summary>
public class MATLABExecutionException : MATLABException
{
    /// <inheritdoc/>
    public MATLABExecutionException()
    {
    }

    /// <inheritdoc/>
    public MATLABExecutionException(string message)
        : base(message)
    {
    }

    /// <inheritdoc/>
    public MATLABExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>An error that occurred while starting, connecting, or communicating with the runtime.</summary>
public class MATLABNotAvailableException : MATLABException
{
    /// <inheritdoc/>
    public MATLABNotAvailableException()
    {
    }

    /// <inheritdoc/>
    public MATLABNotAvailableException(string message)
        : base(message)
    {
    }

    /// <inheritdoc/>
    public MATLABNotAvailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>An error that occurs when a .NET object cannot be converted to a MATLAB type.</summary>
public class UnsupportedTypeException : MATLABException
{
    /// <inheritdoc/>
    public UnsupportedTypeException()
    {
    }

    /// <inheritdoc/>
    public UnsupportedTypeException(string message)
        : base(message)
    {
    }

    /// <inheritdoc/>
    public UnsupportedTypeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
