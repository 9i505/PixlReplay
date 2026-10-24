# Security

## Reporting a vulnerability

Please don't open a public issue for a security problem. Report it privately on GitHub instead: on this repository, open **Security**, then **Report a vulnerability**. Only the maintainer sees it.

Tell us what you found, how to reproduce it, and which PixlReplay version you used (Settings → General, at the foot of the page). We reply as soon as we can and credit you in the release notes if you like.

## Supported versions

Only the latest release gets security fixes. PixlReplay updates itself from inside the app, so the fix reaches you there.

## Getting PixlReplay safely

- Download PixlReplay only from [the releases page](https://github.com/9i55/PixlReplay/releases).
- Each release lists the SHA-256 of its files. To check a download, run this in PowerShell and compare the result with the release page:

  ```powershell
  Get-FileHash .\PixlReplay-Setup.exe
  ```

- Updates come from the same releases page over HTTPS, and Velopack checks each update against the checksum the release lists before putting it in.
- The auto captions engine and model are downloaded only after you agree, and PixlReplay checks their SHA-256 before using them.

## What PixlReplay does with your data

See [Privacy](README.md#privacy) in the README: what PixlReplay keeps on your PC, and the only times it goes online.

## Libraries

PixlReplay is built on libobs (OBS Studio), FFmpeg, ONNX Runtime, whisper.cpp, Velopack and Microsoft Edge WebView2. A problem in one of them is best reported to that project too; PixlReplay ships their fixes in its next release.

---

## الإبلاغ عن ثغرة أمنية

لا تفتح بلاغًا عامًا عن مشكلة أمنية. أبلغ عنها بشكل خاص على GitHub: افتح **Security** في هذا المستودع، ثم **Report a vulnerability**. لا يراها إلا المشرف على المشروع.

اذكر ما وجدته، وكيف يمكن تكرار المشكلة، ورقم إصدار كايروس الذي استخدمته. لا تصل الإصلاحات الأمنية إلا إلى أحدث إصدار، ويحدّث كايروس نفسه من داخل التطبيق.

نزّل كايروس من [صفحة الإصدارات](https://github.com/9i55/PixlReplay/releases) فقط. تذكر كل نسخة بصمة SHA-256 لملفاتها، وتستطيع مطابقتها بالأمر `Get-FileHash .\PixlReplay-Setup.exe` في PowerShell.
