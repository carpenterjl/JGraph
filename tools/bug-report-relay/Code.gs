// The JGraph bug-report relay (ADR 0116). Deployed as a Google Apps Script web app under the
// developer's own account, so no credential of any kind ships inside JGraph itself — the app POSTs
// a report here, and this script sends the email. The URL this deploys to is public by design: the
// only thing it can do is send its owner a bug report.
//
// Deployment steps are in README.md beside this file.

var RECIPIENT = "jacob.carpenter001@gmail.com";
var MAX_BODY = 200 * 1024; // ~200 KB: a title, a description and one attached script.

function doPost(e) {
  try {
    var raw = e && e.postData && e.postData.contents;
    if (!raw || raw.length > MAX_BODY) {
      return reply_({ ok: false, error: "bad request" });
    }

    var r = JSON.parse(raw);
    if (!r.subject || !r.description) {
      return reply_({ ok: false, error: "missing fields" });
    }

    // The subject arrives composed by the app (BugReportSubject) and is used verbatim, so the
    // format the app's tests pin is the format the inbox sees.
    var body = r.description +
      "\n\n---\nApp: " + r.appVersion + "\nOS: " + r.osVersion + "\nUTC: " + r.timestampUtc +
      (r.isCrash ? "\nCRASH REPORT" : "") +
      (r.exception ? "\n\nException:\n" + r.exception : "") +
      (r.scriptText ? "\n\nScript (" + (r.scriptFileName || "untitled") + "):\n" + r.scriptText : "");

    var options = r.replyTo ? { replyTo: String(r.replyTo) } : {};
    MailApp.sendEmail(RECIPIENT, String(r.subject), body, options);
    return reply_({ ok: true });
  } catch (err) {
    return reply_({ ok: false, error: String(err) });
  }
}

function reply_(o) {
  return ContentService.createTextOutput(JSON.stringify(o)).setMimeType(ContentService.MimeType.JSON);
}
