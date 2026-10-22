using System.Windows.Media.Imaging;
using PixlReplay.App;
using PixlReplay.Engine.Share;

namespace PixlReplay.Ui;

public sealed partial class ShareViewModel
{
    public double PreviewVolume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (_volume > 0)
                _muted = false;
            ApplyVolume();
            RaiseVolume();
        }
    }

    public bool IsMuted => _muted || _volume <= 0;

    void ToggleMute()
    {
        if (IsMuted)
        {
            _muted = false;
            if (_volume <= 0)
                _volume = 0.5; // all the way down: unmuting gives some sound back
        }
        else
            _muted = true;
        ApplyVolume();
        RaiseVolume();
    }

    void ApplyVolume() => _host.SetPlaybackVolume(IsMuted ? 0 : _volume);

    void RaiseVolume()
    {
        Raise(nameof(PreviewVolume));
        Raise(nameof(IsMuted));
    }

    /// <summary>The panel closes: the loudness is kept for next time (when it changed).</summary>
    void RememberVolume()
    {
        var prefs = _prefs();
        if (prefs.PreviewVolume == _volume && prefs.PreviewMuted == _muted)
            return;
        var copy = prefs.Copy();
        copy.PreviewVolume = _volume;
        copy.PreviewMuted = _muted;
        _host.Remember(copy);
    }

    // Copy: one press, a check for a moment, nothing else
    public bool IsCopying => _clipCopying;
    public bool IsCopied => _clipCopied;
    bool WholeClip => _info != null && _trimStart <= 0.05 && _trimEnd >= _info.Duration - 0.05;

    void CopyClip()
    {
        if (_picture != null)
        {
            _host.CopyPicture(_clip);
            ShowCopied();
            return;
        }
        if (_info == null || _clipCopying)
            return;
        string source = _clip;
        if (WholeClip)
        {
            // The whole clip: the file itself goes on the clipboard, like copying it in File Explorer.
            _host.CopyToClipboard(source, null);
            ShowCopied();
            return;
        }
        double start = _trimStart, end = _trimEnd;
        string target = ShareFiles.Target(_host.Folder, SharePlatform.Copy, _clip);
        _clipCopying = true;
        RaiseCopy();
        _host.Run(() =>
        {
            try
            {
                var result = _host.Cut(source, target, start, end, new InlineProgress(_ => { }), CancellationToken.None);
                _host.Post(() =>
                {
                    _clipCopying = false;
                    _host.CopyToClipboard(result.Path, null);
                    ShowCopied();
                });
            }
            catch (Exception e)
            {
                _host.Post(() =>
                {
                    _clipCopying = false;
                    _errorDetail = e.Message;
                    _phase = Phase.Failed;
                    RaiseAll();
                });
            }
        });
    }

    void ShowCopied()
    {
        int show = ++_copiedShow;
        _clipCopied = true;
        RaiseCopy();
        _host.After(TimeSpan.FromSeconds(1.8), () =>
        {
            if (show != _copiedShow)
                return;
            _clipCopied = false;
            RaiseCopy();
        });
    }

    void RaiseCopy()
    {
        Raise(nameof(IsCopying));
        Raise(nameof(IsCopied));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Puts <paramref name="emoji"/> in the caption over the selection (or at the caret); returns the caret after it.</summary>
    public int InsertEmoji(string emoji, int selectionStart, int selectionLength)
    {
        int start = Math.Clamp(selectionStart, 0, _caption.Length);
        int length = Math.Clamp(selectionLength, 0, _caption.Length - start);
        Caption = _caption[..start] + emoji + _caption[(start + length)..];
        // The box shows it now (typing in the box needs no such call: the box is where the text came from).
        Raise(nameof(Caption));
        return start + emoji.Length;
    }

    // Watching the cut
    public bool IsPlaying => _playing;
    public string PlayLabel => Text.Get(_playing ? "share.pause" : "share.play");
    /// <summary>The playhead line: while playing, or once it has moved off the start of the cut.</summary>
    public bool ShowPlayhead => _info != null && (_playing || _playhead > _trimStart + 0.05);
    /// <summary>The round play mark in the middle of the paused picture (a click on the picture plays).</summary>
    public bool ShowPlayMark => _info != null && CanEdit && !_playing;

    /// <summary>Where the playback is (or starts): seconds into the clip, always inside the cut. Setting it seeks.</summary>
    public double Playhead
    {
        get => _playhead;
        set
        {
            if (_info == null)
                return;
            _playhead = Math.Clamp(value, _trimStart, _trimEnd);
            if (_playing)
            {
                StopPlayback();
                StartPlayback();
            }
            else
            {
                _previewTime = _playhead;
                RequestPreview();
            }
            RaisePlayhead();
        }
    }

    void RaisePlayhead()
    {
        Raise(nameof(Playhead));
        Raise(nameof(ShowPlayhead));
    }

    /// <summary>From the playhead to the end of the cut; from the start when the playhead is at the end.</summary>
    void StartPlayback()
    {
        if (_info == null || _playing)
            return;
        double from = _playhead >= _trimStart && _playhead < _trimEnd - 0.05 ? _playhead : _trimStart;
        int session = ++_playSession;
        _playing = true;
        _playhead = from;
        int height = (int)Math.Round(PreviewPixels / PreviewAspect) & ~1;
        BitmapSource? picture = null;
        picture = _host.Play(_clip, from, _trimEnd, PreviewPixels, height, t =>
        {
            if (session != _playSession)
                return;
            // The player's picture replaces the still once its first frame is in (no black flash).
            if (picture != null && _previewFrame != picture)
            {
                _previewFrame = picture;
                Raise(nameof(PreviewFrame));
            }
            _playhead = Math.Clamp(t, _trimStart, _trimEnd);
            RaisePlayhead();
        }, _ =>
        {
            if (session != _playSession)
                return;
            _playing = false;
            RaisePlayback();
        });
        RaisePlayback();
    }

    void StopPlayback()
    {
        if (!_playing)
            return;
        _playSession++;
        _playing = false;
        _host.StopPlaying();
        RaisePlayback();
    }

    void RaisePlayback()
    {
        Raise(nameof(IsPlaying));
        Raise(nameof(PlayLabel));
        Raise(nameof(ShowPlayMark));
        RaisePlayhead();
    }

    /// <summary>Small frames along the whole clip under the trim bar (filled in as they are read).</summary>
    public System.Collections.ObjectModel.ObservableCollection<BitmapSource?> Filmstrip { get; } = [];
    public const int FilmstripFrames = 10;

    /// <summary>The frame at the handle being moved (or the start), shown in the big preview.</summary>
    public BitmapSource? PreviewFrame => _picture?.Image ?? _previewFrame ?? _thumbnail;
}
