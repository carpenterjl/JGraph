classdef U6VaultSub < U6Vault
    % What a subclass may and may not reach (u6_access.m).
    methods
        function obj = U6VaultSub(fixed)
            obj@U6Vault(fixed);
        end
        function r = readProt(obj)
            r = obj.Prot;
        end
        function writeProt(obj, v)
            obj.Prot = v;
        end
        function r = readSecret(obj)
            r = obj.Secret;
        end
        function writeSecret(obj, v)
            obj.Secret = v;
        end
        function writeReadOnly(obj, v)
            obj.ReadOnly = v;
        end
        function writeProtSet(obj, v)
            obj.ProtSet = v;
        end
        function writeFixed(obj, v)
            obj.Fixed = v;
        end
        function r = callProt(obj)
            r = prot(obj);
        end
        function r = callPrivFromSub(obj)
            r = priv(obj);
        end
        function r = readShared(obj)
            r = obj.Shared;
        end
        function r = readListed(obj)
            r = obj.Listed;
        end
        function fam(obj)
            notify(obj, 'Family');
        end
        function tickSub(obj)
            notify(obj, 'Tick');
        end
        function lh = listenFam(obj, f)
            lh = addlistener(obj, 'Family', f);
        end
        function r = protOfOther(obj, o)
            r = o.Prot;
        end
    end
end
