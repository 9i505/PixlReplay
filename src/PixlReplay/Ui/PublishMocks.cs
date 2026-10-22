namespace PixlReplay.Ui;

/// <summary>
/// Local pages that behave like the sites' posting pages as seen on 2026-10-13, for tests and --selftest-share-site.
/// Each records what was posted in window.__posted and sets a title starting with "WRONG" when something is done
/// that the real site would punish (posting mid-upload, using the timeline box, pressing a feed button).
/// </summary>
public static class PublishMocks
{
    /// <summary>A sign-in form, as X shows to a signed-out window.</summary>
    public const string SignIn = "<html><body><h1>Sign in</h1><input type=text><input type=password></body></html>";

    /// <summary>X: a timeline composer and the intent dialog; Post stays disabled while the video uploads.</summary>
    public static string X(string caption, bool stuck, bool premium = false) => """
        <html><body>
        <nav><div data-testid=SideNav_AccountSwitcher_Button><span>PixlReplay</span>PREMIUM_MARK</div>PREMIUM_LINK</nav>
        <div id=home><div data-testid=tweetTextarea_0></div><input type=file data-testid=fileInput id=inlineInput><button data-testid=tweetButtonInline aria-disabled=true>Post</button></div>
        <div id=layers><div role=dialog id=dlg>
          <div data-testid=tweetTextarea_0 contenteditable=true id=text>CAPTION</div>
          <input type=file data-testid=fileInput accept="video/mp4" id=dlgInput>
          <div data-testid=attachments id=att></div>
          <button data-testid=tweetButton id=post>Post</button>
        </div></div>
        <script>
          const STUCK = STUCK_FLAG;
          const $ = id => document.getElementById(id);
          $('inlineInput').addEventListener('change', () => document.title = 'WRONG: the timeline box got the file');
          $('dlgInput').addEventListener('change', e => {
            const f = e.target.files[0]; window.__file = f.name + ':' + f.size;
            $('post').setAttribute('aria-disabled', 'true');
            $('att').innerHTML = '<video></video><span>' + f.name + ': Uploading (0%)</span>';
            if (!STUCK) setTimeout(() => { $('att').querySelector('span').textContent = f.name + ': Ready'; $('post').removeAttribute('aria-disabled'); }, 1200);
          });
          $('post').addEventListener('click', () => {
            if ($('post').getAttribute('aria-disabled') === 'true') { document.title = 'WRONG: posted while uploading'; return; }
            if (!window.__file) { document.title = 'WRONG: posted without the video'; return; }
            window.__posted = { text: $('text').innerText, file: window.__file };
            $('dlg').remove();
            const t = document.createElement('div'); t.setAttribute('data-testid', 'toast');
            t.innerHTML = 'Your post was sent. <a href="https://x.com/pixlreplay/status/123">View</a>'; document.body.appendChild(t);
          });
        </script></body></html>
        """.Replace("CAPTION", System.Net.WebUtility.HtmlEncode(caption)).Replace("STUCK_FLAG", stuck ? "true" : "false")
         .Replace("PREMIUM_MARK", premium ? "<svg data-testid=icon-verified width=10 height=10></svg>" : "")
         .Replace("PREMIUM_LINK", premium ? "" : "<a href=\"/i/premium_sign_up\">Premium</a>");

    /// <summary>TikTok Studio: a tip over the page, the editor holding the file name, Post loading, then "Post now".</summary>
    public const string TikTok = """
        <html><body>
        <input type=file accept="video/*" id=up>
        <div id=tip style="position:fixed;left:0;top:0;width:320px;height:180px;background:#fff">New editing features added<button id=gotit>Got it</button></div>
        <script>
          const $ = id => document.getElementById(id);
          $('gotit').addEventListener('click', () => { $('tip').remove(); window.__tipClosed = true; });
          $('up').addEventListener('change', e => {
            const f = e.target.files[0]; window.__file = f.name + ':' + f.size;
            const ed = document.createElement('div'); ed.className = 'public-DraftEditor-content'; ed.contentEditable = 'true';
            ed.textContent = f.name.replace(/\.[^.]+$/, ''); document.body.appendChild(ed);
            const b = document.createElement('button'); b.setAttribute('data-e2e', 'post_video_button'); b.textContent = 'Post';
            b.disabled = true; b.setAttribute('data-loading', 'true'); document.body.appendChild(b);
            // TikTok enables Post a moment before it reports the whole file as uploaded.
            setTimeout(() => { b.disabled = false; b.setAttribute('data-loading', 'false'); }, 600);
            setTimeout(() => { const u = document.createElement('div'); u.textContent = 'Uploaded (1.2MB)'; document.body.appendChild(u); window.__uploaded = true; }, 1500);
            b.addEventListener('click', () => {
              if (b.disabled || !window.__uploaded) { document.title = 'WRONG: posted while uploading'; return; }
              const d = document.createElement('div'); d.setAttribute('role', 'dialog');
              d.innerHTML = 'Continue to post?<button id=postnow>Post now</button>'; document.body.appendChild(d);
              $('postnow').addEventListener('click', () => {
                window.__posted = { text: ed.innerText, file: window.__file }; d.remove();
                const m = document.createElement('div'); m.textContent = 'Your video has been uploaded'; document.body.appendChild(m);
              });
            });
          });
        </script></body></html>
        """;

    /// <summary>
    /// Instagram: Create opens a menu whose "Post" sits deep inside a link; the dialog goes Crop (square by default,
    /// "Select crop" offers Original), Edit, then New reel with the caption box and Share. The caption only counts
    /// once the box has lost focus. The feed has its own "Post" buttons that must never be pressed.
    /// </summary>
    public const string Instagram = """
        <html><body>
        <div id=side><div class=wrap>
          <div><a href="#" role=link id=create><svg aria-label="New post" width=10 height=10></svg><span>Create</span></a></div>
          <div id=menu style="display:none"><a href="#" role=link tabindex=0 id=postlink><div><div><div><div><div><span><span>Post</span></span></div></div></div></div></div><svg width=10 height=10></svg></a></div>
        </div></div>
        <main><article><div role=button id=feedpost><span>Post</span></div><svg width=10 height=10></svg></article></main>
        <script>
          const $ = id => document.getElementById(id);
          let aspect = 'square';
          $('create').addEventListener('click', e => { e.preventDefault(); $('menu').style.display = $('menu').style.display === 'none' ? 'block' : 'none'; });
          $('feedpost').addEventListener('click', () => document.title = 'WRONG: a Post button in the feed was clicked');
          $('postlink').addEventListener('click', e => {
            e.preventDefault();
            const d = document.createElement('div'); d.setAttribute('role', 'dialog');
            d.innerHTML = '<h1>Create new post</h1><input type=file accept="image/jpeg,video/mp4" id=up>';
            document.body.appendChild(d);
            $('up').addEventListener('change', ev => {
              const f = ev.target.files[0]; window.__file = f.name + ':' + f.size;
              d.innerHTML = '<h1>Crop</h1><div role=button id=next>Next</div><div role=button id=crop><svg aria-label="Select crop" width=10 height=10></svg></div>' +
                '<div id=opts style="display:none"><div role=button id=orig><span>Original</span></div><div role=button><span>1:1</span></div></div>';
              $('crop').addEventListener('click', () => $('opts').style.display = 'block');
              $('orig').addEventListener('click', () => aspect = 'original');
              $('next').addEventListener('click', () => {
                d.innerHTML = '<h1>Edit</h1><div role=button id=next2>Next</div>';
                $('next2').addEventListener('click', () => {
                  d.innerHTML = '<h1>New reel</h1><div role=button id=share>Share</div><div contenteditable=true role=textbox aria-label="Write a caption..." id=cap></div>';
                  // Like Instagram (seen 2026-10-13): the caption is saved when the box loses focus; Share sends what was saved.
                  let saved = '';
                  $('cap').addEventListener('blur', () => saved = $('cap').innerText);
                  $('share').addEventListener('click', () => {
                    window.__posted = { text: saved, file: window.__file, aspect };
                    d.innerHTML = '<h1>Reel shared</h1><span>Your reel has been shared.</span>';
                  });
                });
              });
            });
          });
        </script></body></html>
        """;
}
