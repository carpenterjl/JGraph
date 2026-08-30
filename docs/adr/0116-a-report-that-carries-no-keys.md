# ADR 0116 — A report that carries no keys

## Status

Accepted (M114, 2026-08-30).

## Context

Until now the only way a user could report a bug was outside the product, and the only thing an
unhandled crash left behind was a line in `%AppData%\JGraph\crash.log` and a message box asking them
to save and restart. Nothing in `src/` had ever made a network call — the product's data left the
machine only when the user exported a file and carried it somewhere.

A Report a Bug feature has to move a report from the user's machine to the developer's inbox, and
the obvious mechanism — mail credentials for a dedicated account, shipped in the binary — has a
fatal property: anything shipped in a desktop binary can be extracted, and an extracted mail
credential is a spam engine until the provider locks the account. There is no way to store a secret
in a distributed executable; the only winning move is to need no secret.

## Decision

Reports travel to a **relay the developer deploys under their own Google account** — a small Apps
Script web app (`tools/bug-report-relay`) whose URL is a public constant
(`BugReportRelay.Url`). The app POSTs the report as JSON; the relay sends the email. The URL can be
extracted from the binary, and that is fine: the only thing it can do is send its owner a bug
report. Nothing else about this decision works without that inversion, so it is the decision.

What follows from it:

- **The subject line is composed in the app, not the relay.** `BugReportSubject.Format` spells
  `JGraph Bug Report: {title} - DDMMYYYY - {version}` exactly once, a test pins it, and the relay
  uses the string verbatim — the format is a tested fact rather than a hope about deployed
  JavaScript.
- **The report is exactly what the dialog shows.** Title, description, an optional reply-to, the
  version, the OS string, the timestamp, the exception text on the crash path, and — only when the
  checkbox is ticked — the open script. No log tails, no machine or user names, nothing the user
  has not read on the screen in front of them.
- **All of the logic lives in `JGraph.Reporting`**, a plain net8.0 assembly, because the test
  project cannot reference the WPF assemblies. The payload, the subject, the builder and the HTTP
  transport are all testable without a window; the window is a thin shell over them, like every
  other dialog here.
- **The transport never throws across its seam.** `IBugReportTransport.SendAsync` answers
  `BugReportSendResult` for everything from no network to a refusal, and the dialog's fallback is
  Copy Report — the whole report as clipboard text, the one path that needs neither network nor
  relay, so a written report is never simply lost.
- **An unconfigured build degrades, it does not break.** Until the placeholder URL is replaced
  with a deployment, Send is disabled with an explanation and the clipboard path stands in. The
  environment variable `JGRAPH_BUGREPORT_URL` overrides the constant for trying a relay without a
  rebuild.
- **A crash now ends the session through the same dialog.** All three crash guards funnel into it
  prefilled with the exception, and the application exits when it closes — sent or not. This
  supersedes ADR-era behaviour of reporting and carrying on so the user could save: the process
  state is untrusted after an unhandled exception, and the dialog says plainly that JGraph will
  close. Background-thread crashes block on a `Dispatcher.Invoke` so the dialog gets its say
  before the runtime tears the process down; headless runs never see a dialog and still exit with
  the fault in the log, unchanged.

## Consequences

`JGraph.Reporting.dll` ships (it joins the installer's staging anchors so a dropped reference fails
the build rather than the button). The relay's source and its five-minute deployment live in
`tools/bug-report-relay`; the relay was deployed the same day and `BugReportRelay.Url` points at
it — verified end to end, a report sent through the real dialog arriving in the real inbox with
its attachment and Reply-To intact. A build whose constant reverts to a placeholder degrades to a
disabled Send and the clipboard, never to a broken button.

The reply-to the user types is remembered in `settings.json` (`bugReportReplyTo`, nullable, no
format-version bump) so the next report starts with it.

This is the first outbound network call in the product. Anyone auditing where JGraph's data can go
now has exactly one place to look: `HttpBugReportTransport`, invoked only from the bug-report
dialog's Send button, carrying only what that dialog displayed.

No MATLAB-facing behaviour changed, so this adds no divergence.
