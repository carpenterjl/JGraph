function r = ix_imp_nosuch_wild()
% IX_IMP_NOSUCH_WILD  A wildcard import of a namespace that does not exist is accepted and listed.
import No.Such.*
r = import;
end
