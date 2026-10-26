"""
The messages a package's calls raise (compiler and SDK plan, stage K13): R2025b's texts, from its
catalog (resources\\pysdk\\en\\pysdk.xml), put together as its runtime puts them: a message, then a
space and what it names (record-k13.ps1, k13_client: the space ending "'nargout' must be an
integer. " is R2025b's).
"""

MESSAGES = {
    'GeneralPySDKError': 'Error in MATLAB Compiler SDK for Python. Details: {0}',
    'InitErrorWithDetails': 'Error in initializing MATLAB Runtime instance. Details:',
    'ErrorDuringFevalWithNameAndDetails': 'An error occurred during evaluation of the function {0}. Details: {1}',
    'FEvalResultError': 'An error occurred when evaluating the result from a function. Details: {0}',
    'NargoutNotSet': "Wrong number of outputs. If not equal to 1, you must specify it with the 'nargout' keyword.",
    'WrongNargout': "The number of outputs does not match the number specified with the 'nargout' keyword",
    'NargoutMustBeInt': "'nargout' must be an integer.",
    'NargoutCannotBeLessThanZero': "'nargout' must be greater than or equal to 0.",
    'BackgroundMustBeBool': "'background' must be boolean (True or False).",
    'EitherBackgroundOrAsync': "Use either 'background' or 'async'.",
    'AsyncWillBeRemoved': "The 'async' keyword will be removed in a future release. Use 'background' instead.",
    'AsyncMustBeBool': "'async' must be boolean (True or False).",
    'InvalidKwargs': 'Invalid keyword arguments:',
    'OptionIsNotAString': 'Option {0} is not a Python string.',
    'StringIOForConOut': 'A StringIO object is required for console output redirection.',
    'StringIOForConErr': 'A StringIO object is required for console error redirection.',
    'TimeoutShouldBeIntOrFloat': 'Timeout should be a length in seconds as an integer or float.',
    'TimeoutCannotBeNegative': 'Timeout must be greater than or equal to 0.',
    'AttrCannotBeAddedToFunc': 'An attribute cannot be added to a function.',
    'CommandCancelled': 'The command was cancelled.',
    'InterruptedException': 'The execution of the command was interrupted before the result was obtained.',
    'CancelledException': 'The command was already cancelled.',
    'TimeoutException': 'Timeout was reached before command completed.',
    'FunctionNotFoundInPyPkg': 'Function {0} not found in Python package.',
}


def text(name, *args):
    """A message of the catalog, its {0}, {1} filled."""
    return MESSAGES[name].format(*args)


def spaced(name, trailing=''):
    """A message, a space, then what it names: how R2025b's runtime raises its own refusals."""
    return text(name) + ' ' + trailing
