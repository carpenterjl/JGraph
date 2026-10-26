"""The exceptions of the JGraph Runtime's matlab module (compiler and SDK plan, stage K13): what an
array's size or shape does not allow, under the module name R2025b's arrays give them."""


class ShapeError(Exception):
    """An initializer, or the dimensions given to reshape(), whose element count the size does not hold."""


class SizeError(Exception):
    """A nested initializer whose dimensions are not the ones size= gives."""
