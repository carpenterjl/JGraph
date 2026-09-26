function r = ix_imp_var_clash()
% IX_IMP_VAR_CLASH  A variable named like an import is refused when the file is parsed.
import System.Math
Math = 3;
r = Math;
end
