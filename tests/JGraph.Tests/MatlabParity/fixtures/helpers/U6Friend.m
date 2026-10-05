classdef U6Friend
    % Named in U6Vault's access lists (u6_access.m).
    methods
        function r = readShared(obj, v)
            r = v.Shared;
        end
        function writeShared(obj, v, x)
            v.Shared = x;
        end
        function r = readListed(obj, v)
            r = v.Listed;
        end
        function r = callForFriend(obj, v)
            r = forFriend(v);
        end
        function r = callForListed(obj, v)
            r = v.forListed();
        end
        function r = readSecret(obj, v)
            r = v.Secret;
        end
        function r = readProt(obj, v)
            r = v.Prot;
        end
    end
end
