# The bug-report relay

`Code.gs` is the server half of JGraph's Report a Bug feature (ADR 0116). It runs as a Google Apps
Script **web app** under your own Google account: JGraph POSTs a report to its URL, and the script
emails the report to you. Because the mail is sent by the script — running as you, on Google's
side — **no password, app password, token or credential of any kind exists in JGraph's binary.**
The URL is the only thing the app knows, and the only thing the URL can do is send you a bug
report.

## Deploying it (one time, about five minutes)

1. Open <https://script.google.com> (signed in as the account that should send the mail) and
   choose **New project**.
2. Delete the placeholder code and paste in the whole of `Code.gs`. Name the project something
   like *JGraph bug reports*.
3. **Deploy → New deployment**. Click the gear beside *Select type* and choose **Web app**.
4. Set **Execute as: Me** and **Who has access: Anyone**. (That is what lets JGraph POST without
   any sign-in. "Anyone" here means anyone may *send you a bug report* — they run the script as
   you only in the sense that the mail comes from your account to your account.)
5. Click **Deploy** and authorize the permission it asks for (sending email as you).
6. Copy the web app URL — it looks like
   `https://script.google.com/macros/s/AKfycb…/exec`.

## Pointing JGraph at it

Paste the URL into `Url` in [`src/JGraph.Reporting/BugReportRelay.cs`](../../src/JGraph.Reporting/BugReportRelay.cs)
and rebuild. Until that constant changes, Send stays disabled in every build and the dialog offers
Copy Report instead.

To try a deployment without rebuilding, set the environment variable `JGRAPH_BUGREPORT_URL` to the
`/exec` URL and start JGraph — the variable wins over the constant.

A quick smoke test from PowerShell (expects `{"ok":true}` back and a mail in the inbox):

```powershell
Invoke-RestMethod -Method Post -Uri "<your /exec url>" -ContentType "application/json" -Body (@{
  subject = "JGraph Bug Report: relay smoke test - 01012026 - 0.0.0"
  description = "If you can read this, the relay works."
  appVersion = "0.0.0"; osVersion = "test"; timestampUtc = "2026-01-01T00:00:00Z"; isCrash = $false
} | ConvertTo-Json)
```

## Things worth knowing

- **Quota**: a consumer Gmail account may send roughly 100 emails a day through `MailApp`. Past
  that, sends fail until the quota resets — the app shows the failure and offers the clipboard.
- **Editing the script later**: changes are not live until you **Deploy → Manage deployments →
  edit (pencil) → Version: New version → Deploy**. The URL stays the same.
- **Abuse**: the URL can be extracted from the binary — that is fine and expected. The worst
  anyone can do with it is send you email. If someone floods you, delete the deployment (the URL
  dies with it), redeploy to get a fresh URL, and ship the new constant.
- **Reply-to**: when a reporter leaves their email, it arrives as the mail's Reply-To, so
  answering them is just pressing Reply.
