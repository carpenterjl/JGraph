function v = oi169_locallock()
% A local function locks: the file is locked, asked bare and by name (oi169_session_questions).
inner();
v = [mislocked, mislocked('oi169_locallock')];
end

function inner()
mlock;
end
