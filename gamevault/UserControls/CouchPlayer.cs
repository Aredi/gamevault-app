using Avalonia.Controls;
using gamevault.Helper;
using gamevault.Helper.Platform;
using GameVault.Core;
using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// The living-room mode's player: a page with the video alone (no buttons, no bar), YouTube's separate sound kept in
    /// step with it, or a sound alone (a game's theme). It says whether it is heard, so the menu's music stops only then.
    /// </summary>
    internal sealed class CouchPlayer : Border
    {
        internal enum State { Off, Loading, Heard, Silent, Ended, Failed }

        private readonly NativeWebView? web;
        private bool started;

        public CouchPlayer()
        {
            Background = Avalonia.Media.Brushes.Black;
            try
            {
                web = new NativeWebView();
                NativeWebViewPlacement.KeepPlaced(web);
                Child = web;
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>What it does now, as the page last said (twice a second through <see cref="ReadStateAsync"/>).</summary>
        internal State Now { get; private set; }

        /// <param name="video">The picture's address; null: the sound alone.</param>
        /// <param name="sound">The sound's address when it is apart (YouTube); empty: the video's own.</param>
        internal void Play(string? video, string sound, double volume, bool loop = false)
        {
            if (web == null)
            {
                Now = State.Failed;
                return;
            }
            Now = State.Loading;
            started = true;
            try { web.NavigateToString(Page(video, sound, volume, loop)); }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                Now = State.Failed;
            }
        }

        internal void Stop()
        {
            Now = State.Off;
            if (!started || web == null)
                return;
            started = false;
            try { web.NavigateToString("<html><body style='background:#000'></body></html>"); }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        internal async Task<State> ReadStateAsync()
        {
            if (Now is State.Off or State.Failed || web == null)
                return Now;
            try
            {
                string? said = await web.InvokeScript("window.couchState || 'loading'");
                // Some engines give the string back in quotes
                Now = said?.Trim('"') switch
                {
                    "heard" => State.Heard,
                    "silent" => State.Silent,
                    "ended" => State.Ended,
                    "failed" => State.Failed,
                    _ => State.Loading,
                };
            }
            catch (Exception ex) { Log.Ignored(ex); }
            return Now;
        }

        /// <summary>The page: the picture fills the frame, fades in once it plays; heard when the sound plays, silent when the engine refused it.</summary>
        internal static string Page(string? video, string sound, double volume, bool loop)
        {
            string v = video == null ? "" : WebUtility.HtmlEncode(video);
            string a = WebUtility.HtmlEncode(sound);
            string level = Math.Clamp(volume, 0, 1).ToString("0.##", CultureInfo.InvariantCulture);
            string media = video == null
                ? $"<audio id='a' src='{a}' preload='auto'{(loop ? " loop" : "")}></audio>"
                : $"<video id='v' src='{v}' playsinline preload='auto'{(loop ? " loop" : "")}></video>" + (sound.Length > 0 ? $"<audio id='a' src='{a}' preload='auto'></audio>" : "");
            return $$$"""
<!DOCTYPE html><html><head><meta charset='utf-8'><style>
html,body{margin:0;width:100%;height:100%;background:#000;overflow:hidden}
video{width:100%;height:100%;object-fit:cover;opacity:0;transition:opacity .6s ease}
video.on{opacity:1}
</style></head><body>{{{media}}}<script>
var v=document.getElementById('v'),a=document.getElementById('a'),level={{{level}}};
window.couchState='loading';
function said(s){if(window.couchState!=='ended')window.couchState=s;}
function heard(el){el.volume=level;el.muted=false;return el.play().then(function(){said('heard');});}
if(v&&a){
  v.muted=true;
  v.addEventListener('playing',function(){v.className='on';if(a.paused){a.currentTime=v.currentTime;heard(a).catch(function(){said('silent');});}});
  v.addEventListener('waiting',function(){a.pause();});
  v.addEventListener('pause',function(){a.pause();});
  v.addEventListener('ended',function(){a.pause();window.couchState='ended';});
  setInterval(function(){if(!v.paused&&!a.paused&&Math.abs(a.currentTime-v.currentTime)>0.3)a.currentTime=v.currentTime;},2000);
  v.play().catch(function(){said('failed');});
}else if(v){
  v.addEventListener('playing',function(){v.className='on';});
  v.addEventListener('ended',function(){window.couchState='ended';});
  heard(v).catch(function(){v.muted=true;v.play().then(function(){said('silent');}).catch(function(){said('failed');});});
}else if(a){
  a.addEventListener('ended',function(){window.couchState='ended';});
  heard(a).catch(function(){said('failed');});
}
(v||a).addEventListener('error',function(){said('failed');});
</script></body></html>
""";
        }
    }
}
