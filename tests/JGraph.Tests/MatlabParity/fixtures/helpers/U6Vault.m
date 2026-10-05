classdef U6Vault < handle
    % Every kind of member access (u6_access.m).
    properties
        Open = 1
    end
    properties (Access = private)
        Secret = 42
    end
    properties (SetAccess = private)
        ReadOnly = 'ro'
    end
    properties (GetAccess = private)
        WriteOnly = 'wo'
    end
    properties (Access = protected)
        Prot = 3
    end
    properties (SetAccess = protected)
        ProtSet = 4
    end
    properties (SetAccess = immutable)
        Fixed = 5
    end
    properties (Access = ?U6Friend)
        Shared = 6
    end
    properties (GetAccess = {?U6Friend, ?U6Other}, SetAccess = private)
        Listed = 7
    end
    properties (Hidden)
        Hid = 8
    end
    properties (Constant, Access = private)
        K = 9
    end
    properties (Access = 'private')
        Quoted = 10
    end
    properties (GetAccess = public, SetAccess = private)
        Both = 11
    end
    events (NotifyAccess = private)
        Tick
    end
    events (ListenAccess = private)
        Quiet
    end
    events (ListenAccess = protected, NotifyAccess = protected)
        Family
    end
    methods
        function obj = U6Vault(fixed)
            if nargin > 0
                obj.Fixed = fixed;
            end
        end
        function r = peek(obj)
            r = obj.Secret;
        end
        function poke(obj, v)
            obj.Secret = v;
        end
        function setReadOnly(obj, v)
            obj.ReadOnly = v;
        end
        function r = getWriteOnly(obj)
            r = obj.WriteOnly;
        end
        function setFixed(obj, v)
            obj.Fixed = v;
        end
        function r = callPriv(obj)
            r = priv(obj);
        end
        function r = callPrivDot(obj)
            r = obj.priv();
        end
        function r = other(obj, o)
            r = o.Secret + priv(o);
        end
        function r = constK(obj)
            r = obj.K + U6Vault.K;
        end
        function r = viaLocal(obj)
            r = u6vault_local(obj);
        end
        function h = handleTo(obj)
            h = @priv;
        end
        function h = anonTo(obj)
            h = @() priv(obj);
        end
        function h = boundTo(obj)
            h = @obj.priv;
        end
        function h = protHandle(obj)
            h = @prot;
        end
        function tick(obj)
            notify(obj, 'Tick');
        end
        function lh = listenQuiet(obj, f)
            lh = addlistener(obj, 'Quiet', f);
        end
        function quiet(obj)
            notify(obj, 'Quiet');
        end
    end
    methods (Access = private)
        function r = priv(obj)
            r = obj.Secret + 1;
        end
    end
    methods (Access = protected)
        function r = prot(obj)
            r = obj.Prot + 1;
        end
    end
    methods (Access = ?U6Friend)
        function r = forFriend(obj)
            r = 'friend';
        end
    end
    methods (Access = {?U6Friend, ?U6Other})
        function r = forListed(obj)
            r = 'listed';
        end
    end
    methods (Hidden)
        function r = hid(obj)
            r = 'hidden';
        end
    end
    methods (Static, Access = private)
        function r = sPriv()
            r = 'spriv';
        end
    end
    methods (Static)
        function r = sPub()
            r = U6Vault.sPriv();
        end
    end
end

function r = u6vault_local(obj)
r = obj.Secret + priv(obj);
end
