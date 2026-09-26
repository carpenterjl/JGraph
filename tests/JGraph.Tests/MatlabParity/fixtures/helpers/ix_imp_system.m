function r = ix_imp_system()
% IX_IMP_SYSTEM  import System.* in a function: namespaces, types and enums below it resolve.
import System.*
r = {
    'list',       ix_show(import)
    'Math',       ix_try(@() Math.Max(1, 2))
    'String',     ix_try(@() class(String('x')))
    'nested_ns',  ix_try(@() class(Collections.ArrayList()))
    'DayOfWeek',  ix_try(@() DayOfWeek.Monday)
    'string_fn',  ix_try(@() class(string('x')))
    };
end
