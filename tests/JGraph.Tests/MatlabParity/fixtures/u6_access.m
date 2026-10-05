% U6 of the app-building plan (ADR 0203): who may read a property, write it, call a method,
% listen to an event or raise one. U6Vault holds one member of every kind of access; the script,
% a subclass, a class named in an access list, a subclass of that one and a stranger each try.
global vlog_text
vlog_text = '';
v = U6Vault(50);

% --- properties, from the script -----------------------------------------------------------------
u2_chk('open_read', @() v.Open);
u2_chk('open_write', @() u6_getp(u6_setp(v, 'Open', 2), 'Open'));
u2_chk('secret_read', @() v.Secret);
u2_chk('secret_write', @() u6_setp(v, 'Secret', 1));
u2_chk('secret_peek', @() v.peek());
v.poke(43);
u2_chk('secret_poke', @() peek(v));
u2_chk('readonly_read', @() v.ReadOnly);
u2_chk('readonly_write', @() u6_setp(v, 'ReadOnly', 'x'));
v.setReadOnly('set');
u2_chk('readonly_by_method', @() v.ReadOnly);
u2_chk('writeonly_read', @() v.WriteOnly);
u2_chk('writeonly_write', @() class(u6_setp(v, 'WriteOnly', 'w2')));
u2_chk('writeonly_by_method', @() v.getWriteOnly());
u2_chk('protected_read', @() v.Prot);
u2_chk('protected_write', @() u6_setp(v, 'Prot', 1));
u2_chk('protected_set_read', @() v.ProtSet);
u2_chk('protected_set_write', @() u6_setp(v, 'ProtSet', 1));
u2_chk('immutable_read', @() v.Fixed);
u2_chk('immutable_write', @() u6_setp(v, 'Fixed', 1));
u5_run('immutable_by_method', @() v.setFixed(1));
u2_chk('immutable_kept', @() v.Fixed);
u2_chk('listed_one_read', @() v.Shared);
u2_chk('listed_one_write', @() u6_setp(v, 'Shared', 1));
u2_chk('listed_two_read', @() v.Listed);
u2_chk('hidden_read', @() v.Hid);
u2_chk('hidden_write', @() u6_getp(u6_setp(v, 'Hid', 80), 'Hid'));
u2_chk('constant_private', @() U6Vault.K);
u2_chk('constant_private_instance', @() v.K);
u2_chk('constant_private_inside', @() v.constK());
u2_chk('quoted_read', @() v.Quoted);
u2_chk('both_read', @() v.Both);
u2_chk('both_write', @() u6_setp(v, 'Both', 1));

% --- what the lists show -------------------------------------------------------------------------
u2_chk('properties', @() properties(v)');
u2_chk('fieldnames', @() fieldnames(v)');
u2_chk('isprop_public', @() isprop(v, 'Open'));
u2_chk('isprop_private', @() isprop(v, 'Secret'));
u2_chk('isprop_protected', @() isprop(v, 'Prot'));
u2_chk('isprop_hidden', @() isprop(v, 'Hid'));
u2_chk('isprop_readonly', @() isprop(v, 'ReadOnly'));
u2_chk('isprop_writeonly', @() isprop(v, 'WriteOnly'));
u2_chk('methods', @() sort(methods(v))');
u2_chk('ismethod_public', @() ismethod(v, 'peek'));
u2_chk('ismethod_private', @() ismethod(v, 'priv'));
u2_chk('ismethod_protected', @() ismethod(v, 'prot'));
u2_chk('ismethod_hidden', @() ismethod(v, 'hid'));
u2_chk('ismethod_listed', @() ismethod(v, 'forFriend'));
u2_chk('ismethod_static_private', @() ismethod(v, 'sPriv'));
u2_chk('ismethod_static_public', @() ismethod(v, 'sPub'));
u2_chk('events', @() events(v)');
shown = evalc('disp(v)');
u2_chk('disp', @() u6_disp(shown));

% --- methods, from the script --------------------------------------------------------------------
u2_chk('private_fn', @() priv(v));
u2_chk('private_dot', @() v.priv());
u2_chk('private_inside', @() v.callPriv());
u2_chk('private_inside_dot', @() v.callPrivDot());
u2_chk('private_other_instance', @() v.other(U6Vault));
u2_chk('private_local_function', @() v.viaLocal());
u2_chk('private_feval', @() feval('priv', v));
u2_chk('protected_fn', @() prot(v));
u2_chk('protected_dot', @() v.prot());
u2_chk('hidden_fn', @() hid(v));
u2_chk('listed_one_fn', @() forFriend(v));
u2_chk('listed_two_fn', @() forListed(v));
u2_chk('static_private', @() U6Vault.sPriv());
u2_chk('static_public', @() U6Vault.sPub());

% --- a handle made inside the class keeps the class's access -------------------------------------
h = v.handleTo();
u2_chk('handle_bare', @() h(v));
h = v.anonTo();
u2_chk('handle_anonymous', @() h());
h = v.boundTo();
u2_chk('handle_bound', @() h());
h = v.protHandle();
u2_chk('handle_protected', @() h(v));
h = @priv;
u2_chk('handle_made_outside', @() h(v));
h = @() priv(v);
u2_chk('handle_anonymous_outside', @() h());

% --- a subclass ----------------------------------------------------------------------------------
vs = U6VaultSub(1);
u2_chk('sub_reads_protected', @() vs.readProt());
u5_run('sub_writes_protected', @() vs.writeProt(30));
u2_chk('sub_protected_written', @() vs.readProt());
u2_chk('sub_reads_private', @() vs.readSecret());
u5_run('sub_writes_private', @() vs.writeSecret(1));
u5_run('sub_writes_readonly', @() vs.writeReadOnly('s'));
u5_run('sub_writes_protected_set', @() vs.writeProtSet(40));
u2_chk('sub_protected_set_written', @() vs.ProtSet);
u5_run('sub_writes_immutable', @() vs.writeFixed(2));
u2_chk('sub_calls_protected', @() vs.callProt());
u2_chk('sub_calls_private', @() vs.callPrivFromSub());
u2_chk('sub_reads_listed_one', @() vs.readShared());
u2_chk('sub_reads_listed_two', @() vs.readListed());
u2_chk('sub_protected_of_base_instance', @() vs.protOfOther(v));
u2_chk('sub_inherited_peek', @() vs.peek());
u2_chk('sub_private_outside', @() vs.Secret);
u2_chk('sub_protected_outside', @() vs.Prot);
u2_chk('sub_protected_fn_outside', @() prot(vs));
u2_chk('sub_properties', @() properties(vs)');

% --- a class named in the lists, its subclass, and one named in only some ------------------------
f = U6Friend;
fs = U6FriendSub;
o = U6Other;
u2_chk('friend_reads_listed_one', @() f.readShared(v));
u5_run('friend_writes_listed_one', @() f.writeShared(v, 60));
u2_chk('friend_wrote_listed_one', @() f.readShared(v));
u2_chk('friend_reads_listed_two', @() f.readListed(v));
u2_chk('friend_calls_listed_one', @() f.callForFriend(v));
u2_chk('friend_calls_listed_two', @() f.callForListed(v));
u2_chk('friend_reads_private', @() f.readSecret(v));
u2_chk('friend_reads_protected', @() f.readProt(v));
u2_chk('friend_sub_reads_listed_one', @() fs.subReadShared(v));
u2_chk('friend_sub_calls_listed_one', @() fs.subCallForFriend(v));
u2_chk('friend_sub_inherited_read', @() fs.readShared(v));
u2_chk('other_reads_listed_two', @() o.readListed(v));
u2_chk('other_reads_listed_one', @() o.readShared(v));
u2_chk('other_calls_listed_two', @() o.callForListed(v));
u2_chk('other_calls_listed_one', @() o.callForFriend(v));

% --- events --------------------------------------------------------------------------------------
vlog_text = '';
u2_chk('listen_public', @() class(addlistener(v, 'Tick', @(~, ~) vlog('tick'))));
u2_chk('listen_private_outside', @() class(addlistener(v, 'Quiet', @(~, ~) vlog('quiet-out'))));
u2_chk('listen_private_inside', @() class(v.listenQuiet(@(~, ~) vlog('quiet'))));
u2_chk('listen_protected_outside', @() class(addlistener(v, 'Family', @(~, ~) vlog('family-out'))));
u2_chk('listen_protected_sub', @() class(vs.listenFam(@(~, ~) vlog('family'))));
u5_run('notify_private_outside', @() notify(v, 'Tick'));
u5_run('notify_private_inside', @() v.tick());
u5_run('notify_private_sub', @() vs.tickSub());
u5_run('notify_public_inside', @() v.quiet());
u5_run('notify_protected_outside', @() notify(v, 'Family'));
u5_run('notify_protected_sub', @() vs.fam());
u2_chk('event_log', @() u6_log());

% --- a value class -------------------------------------------------------------------------------
bx = U6VBox;
bx = bx.inc();
u2_chk('value_total', @() bx.total());
u2_chk('value_readonly_read', @() bx.N);
u2_chk('value_readonly_write', @() u6_setp(bx, 'N', 5));
u2_chk('value_private_read', @() bx.P);
u2_chk('value_properties', @() properties(bx)');

% --- a private constructor -----------------------------------------------------------------------
u2_chk('private_constructor', @() U6Single());
u2_chk('private_constructor_static', @() class(U6Single.instance()));
u2_chk('private_constructor_same', @() U6Single.instance() == U6Single.instance());
