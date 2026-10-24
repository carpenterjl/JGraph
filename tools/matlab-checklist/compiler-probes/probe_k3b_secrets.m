function probe_k3b_secrets()
%PROBE_K3B_SECRETS What R2025b's secret functions answer in a matlab -batch session with names the
%vault does not hold (compiler and SDK plan, stage K3b): getSecret, isSecret, listSecrets,
%removeSecret, getSecretMetadata, setSecretMetadata, setSecret's refusals, secretID, and the texts
%of the messages they raise. Nothing is written to the vault.
beep('off');
addpath(fullfile(fileparts(mfilename('fullpath')), 'src'));

k0_try('isSecret.missing', @() isSecret('k3bnosuch'));
k0_try('isSecret.string', @() isSecret("k3bnosuch"));
k0_try('isSecret.empty', @() isSecret(''));
k0_try('isSecret.number', @() isSecret(5));
k0_try('isSecret.noargs', @() isSecret());
k0_try('isSecret.cell', @() isSecret({'a'}));
k0_try('getSecret.missing', @() getSecret('k3bnosuch'));
k0_try('getSecret.empty', @() getSecret(''));
k0_try('getSecret.emptystring', @() getSecret(""));
k0_try('getSecret.number', @() getSecret(5));
k0_try('getSecret.noargs', @() getSecret());
k0_try('getSecret.two', @() getSecret('a', 'b'));
k0_try('getSecret.strings', @() getSecret(["a" "b"]));
k0_try('getSecret.colon', @() getSecret('a:b'));
k0_try('getSecret.space', @() getSecret('a b'));
k0_try('getSecret.path', @() getSecret('PATH'));
k0_try('getSecretMetadata.missing', @() getSecretMetadata('k3bnosuch'));
k0_do('removeSecret.missing', @() removeSecret('k3bnosuch'));
k0_do('removeSecret.empty', @() removeSecret(''));
k0_do('setSecretMetadata.missing', @() setSecretMetadata('k3bnosuch', dictionary("a", "b")));
k0_do('setSecretMetadata.cells', @() setSecretMetadata('k3bnosuch', dictionary("a", {1})));
k0_do('setSecretMetadata.unconfigured', @() setSecretMetadata('k3bnosuch', dictionary()));
k0_do('setSecretMetadata.numkeys', @() setSecretMetadata('k3bnosuch', dictionary(1, {1})));
k0_do('setSecretMetadata.number', @() setSecretMetadata('k3bnosuch', 5));
k0_do('setSecretMetadata.one', @() setSecretMetadata('k3bnosuch'));
k0_do('setSecret.empty', @() setSecret(''));
k0_do('setSecret.number', @() setSecret(5));
k0_do('setSecret.badopt', @() setSecret('k3bnosuch', 'Nosuch', 1));
k0_try('listSecrets.class', @() class(listSecrets));
k0_try('listSecrets.vars', @() strjoin(listSecrets.Properties.VariableNames, ','));
k0_try('listSecrets.has.probe', @() any(listSecrets.SecretName == "k3bnosuch"));
k0_try('listSecrets.arg', @() listSecrets(1));
k0_try('secretID.class', @() class(secretID('k3bnosuch')));
k0_try('secretID.name', @() secretID('k3bnosuch').Name);
k0_try('secretID.disp', @() strtrim(evalc('disp(secretID(''k3bnosuch''))')));
k0_try('secretID.display', @() strtrim(evalc('s = secretID("k3bnosuch")')));
k0_try('secretID.empty', @() secretID(''));
k0_try('secretID.number', @() secretID(5));
k0_try('secretID.getSecret', @() getSecret(secretID('k3bnosuch')));
k0_try('secretID.set', @() setName(secretID('k3bnosuch')));
k0_try('getSecret.noout', @() strtrim(evalc('getSecret(''k3bnosuch'')')));

ids = {
    {'MATLAB:authnz:secretapis:SecretValueNotFound', 'NAME'}
    {'MATLAB:authnz:secretapis:KeyAlreadyExists', 'NAME'}
    {'MATLAB:authnz:secretapis:VaultNotSupported', 'getSecret'}
    {'MATLAB:authnz:secretapis:VaultNotFound', 'getSecret'}
    {'MATLAB:authnz:secretapis:NoSecretsToList'}
    {'MATLAB:authnz:secretapis:PromptTitle'}
    {'MATLAB:authnz:secretapis:PromptMessage', 'NAME'}
    {'MATLAB:authnz:secretapis:SecretMetadataNotFound', 'NAME'}
    {'MATLAB:authnz:secretapis:InvalidSecretName'}
    };
for i = 1:numel(ids)
    a = ids{i};
    k0_try(['msg.' a{1}], @() getString(message(a{:})));
end
end

function setName(s)
s.Name = "other";
end
