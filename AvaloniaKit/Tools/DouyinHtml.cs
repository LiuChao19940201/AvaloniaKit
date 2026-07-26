namespace AvaloniaKit.Tools;

// ══════════════════════════════════════════════════════════════════════════════
//  DouyinHtml — 抖音短视频页面（一份 HTML，三端复用）
//  · 视频源：公开短视频 API（302 → CDN mp4），video 标签直连不受 CORS 限制
//  · 界面还原：全屏竖屏视频、右侧头像/点赞/评论/收藏/分享/唱片、底部作者与
//    文案、音乐跑马灯、底部细进度条、加载转圈
//  · 交互：上滑/下滑（触摸+滚轮）切换视频、单击暂停/播放、双击飘红心点赞
//  · 返回：左上角按钮 → iframe 场景 postMessage('douyin-exit')，
//    WebView 场景导航 app://exit（由平台层拦截）
// ══════════════════════════════════════════════════════════════════════════════
public static class DouyinHtml
{
    public const string Page = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8"/>
<meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no"/>
<style>
  * { margin:0; padding:0; box-sizing:border-box; -webkit-tap-highlight-color:transparent; }
  html,body { width:100%; height:100%; overflow:hidden; background:#000;
              font-family:-apple-system,'PingFang SC','Microsoft YaHei',sans-serif;
              user-select:none; -webkit-user-select:none; }
  #stage { position:fixed; inset:0; background:#000; }
  video { position:absolute; inset:0; width:100%; height:100%;
          object-fit:contain; background:#000; transition:opacity .18s; }

  /* 顶部返回 + 标题 */
  .topbar { position:fixed; top:0; left:0; right:0; height:64px; z-index:30;
            display:flex; align-items:center; padding:14px 6px 0 6px;
            background:linear-gradient(#00000088,transparent); }
  .back { width:44px; height:44px; display:flex; align-items:center; justify-content:center;
          color:#fff; font-size:24px; }
  .tabs { flex:1; display:flex; justify-content:center; gap:22px;
          color:#ffffffa0; font-size:16px; margin-right:44px; }
  .tabs .on { color:#fff; font-weight:600; position:relative; }
  .tabs .on::after { content:''; position:absolute; left:50%; transform:translateX(-50%);
                     bottom:-7px; width:22px; height:3px; border-radius:2px; background:#fff; }

  /* 右侧操作栏 */
  .side { position:fixed; right:10px; bottom:120px; z-index:20;
          display:flex; flex-direction:column; align-items:center; gap:20px; }
  .avatar { width:50px; height:50px; border-radius:50%; border:2px solid #fff;
            background:linear-gradient(135deg,#7F7FD5,#86A8E7,#91EAE4);
            display:flex; align-items:center; justify-content:center;
            color:#fff; font-size:20px; font-weight:700; position:relative; }
  .avatar .plus { position:absolute; bottom:-9px; left:50%; transform:translateX(-50%);
                  width:19px; height:19px; border-radius:50%; background:#FE2C55;
                  color:#fff; font-size:14px; line-height:19px; text-align:center; }
  .act { display:flex; flex-direction:column; align-items:center; gap:3px; color:#fff; }
  .act .ico { font-size:32px; filter:drop-shadow(0 1px 3px #0008); transition:transform .12s; }
  .act .num { font-size:12px; color:#fffffff0; }
  .act.liked .ico { color:#FE2C55; }
  .disc { width:46px; height:46px; border-radius:50%; margin-top:4px;
          background:radial-gradient(circle at 50% 50%, #444 18%, #111 60%);
          border:6px solid #222; display:flex; align-items:center; justify-content:center;
          font-size:16px; animation:spin 6s linear infinite; }
  @keyframes spin { to { transform:rotate(360deg); } }

  /* 底部信息 */
  .info { position:fixed; left:14px; right:86px; bottom:44px; z-index:20; color:#fff; }
  .who  { font-size:17px; font-weight:700; text-shadow:0 1px 3px #0008; }
  .txt  { margin-top:7px; font-size:14px; line-height:1.5; color:#ffffffe8;
          text-shadow:0 1px 3px #0008; display:-webkit-box; -webkit-line-clamp:2;
          -webkit-box-orient:vertical; overflow:hidden; }
  .music { margin-top:9px; display:flex; align-items:center; gap:6px;
           font-size:13px; color:#ffffffd0; width:70%; overflow:hidden; }
  .music .mq { white-space:nowrap; animation:mq 7s linear infinite; }
  @keyframes mq { 0%{transform:translateX(100%)} 100%{transform:translateX(-100%)} }

  /* 进度条 */
  .prog { position:fixed; left:0; right:0; bottom:0; height:2.5px; z-index:25; background:#ffffff22; }
  .prog i { display:block; height:100%; width:0; background:#ffffffcc; }

  /* 加载/暂停/飘心 */
  .spin { position:fixed; left:50%; top:50%; z-index:26; width:34px; height:34px;
          margin:-17px 0 0 -17px; border-radius:50%; border:3px solid #ffffff30;
          border-top-color:#fff; animation:spin 0.8s linear infinite; display:none; }
  .pauseIco { position:fixed; left:50%; top:50%; z-index:26; transform:translate(-50%,-50%);
              font-size:64px; color:#ffffff90; display:none; text-shadow:0 2px 8px #0006; }
  .heart { position:fixed; z-index:27; font-size:74px; color:#FE2C55; pointer-events:none;
           animation:pop 0.85s ease-out forwards; text-shadow:0 2px 10px #0005; }
  @keyframes pop { 0%{transform:scale(0) rotate(-14deg); opacity:0}
                   18%{transform:scale(1.25) rotate(6deg); opacity:1}
                   45%{transform:scale(1)} 100%{transform:scale(1) translateY(-90px); opacity:0} }
  .toast { position:fixed; left:50%; top:14%; transform:translateX(-50%); z-index:28;
           background:#000000b0; color:#fff; font-size:13px; padding:8px 16px;
           border-radius:18px; opacity:0; transition:opacity .25s; }
</style>
</head>
<body>
<div id="stage">
  <video id="vd" playsinline webkit-playsinline preload="auto"></video>
</div>

<div class="topbar">
  <div class="back" id="btnBack">&#10094;</div>
  <div class="tabs"><span>关注</span><span class="on">推荐</span></div>
</div>

<div class="side">
  <div class="avatar" id="avatar">A<span class="plus">+</span></div>
  <div class="act" id="btnLike"><span class="ico">&#10084;</span><span class="num" id="numLike">12.3w</span></div>
  <div class="act"><span class="ico">&#128172;</span><span class="num" id="numCmt">8592</span></div>
  <div class="act"><span class="ico">&#11088;</span><span class="num" id="numFav">2.1w</span></div>
  <div class="act"><span class="ico">&#10148;</span><span class="num" id="numShare">6034</span></div>
  <div class="disc">&#127925;</div>
</div>

<div class="info">
  <div class="who" id="who">@小可爱</div>
  <div class="txt" id="txt">记录美好生活～</div>
  <div class="music">&#127926;<div class="mq" id="mq">创作的原声 - 抖音热门BGM</div></div>
</div>

<div class="prog"><i id="bar"></i></div>
<div class="spin" id="spin"></div>
<div class="pauseIco" id="pauseIco">&#9654;</div>
<div class="toast" id="toast"></div>

<script>
(function(){
  // ── 视频源池：公开短视频 API（302 → CDN mp4），带随机参数防缓存 ──
  var apis = [
    'https://api.yujn.cn/api/zzxjj.php?type=video',
    'https://api.yujn.cn/api/xjj.php?type=video',
    'https://api.yujn.cn/api/nvda.php?type=video',
    'https://api.yujn.cn/api/manhuay.php?type=video'
  ];
  var names  = ['小可爱','时光机','旅行日记','美食家阿伟','街拍先生','山海故事','奶茶不加冰','慢生活研究所'];
  var texts  = ['记录美好生活的每一个瞬间 #日常 #治愈','这条视频拍了三个小时，值了！#vlog',
                '谁能拒绝这样的风景呢 #旅行 #风光','今日份快乐已送达 #开心 #日常碎片',
                '第一次尝试这样拍，效果意外的好 #创意','慢下来，生活其实很美 #治愈系 #慢生活',
                '这个BGM也太上头了吧 #音乐 #热门','周末的正确打开方式 #周末 #放松'];
  var musics = ['创作的原声 - 抖音热门BGM','夏天的风 - 温柔女声版','Sunny Day - Chill Beats',
                '人间烟火 - 治愈钢琴曲','热门卡点BGM - DJ版','晚风轻拂 - 民谣弹唱'];

  var vd = document.getElementById('vd'), bar = document.getElementById('bar');
  var spin = document.getElementById('spin'), pauseIco = document.getElementById('pauseIco');
  var toast = document.getElementById('toast');
  var idx = 0, liked = false, toastTimer = 0;

  function rndNum(){ var n = Math.random()*30; return n>10 ? n.toFixed(1)+'w' : Math.floor(n*9000+500)+''; }
  function pick(a){ return a[Math.floor(Math.random()*a.length)]; }

  function showToast(t){
    toast.textContent = t; toast.style.opacity = 1;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function(){ toast.style.opacity = 0; }, 1300);
  }

  // ── 换一条视频（随机 API + 随机文案/数字） ──
  function load(){
    liked = false;
    var like = document.getElementById('btnLike');
    like.classList.remove('liked');
    document.getElementById('numLike').textContent = rndNum();
    document.getElementById('numCmt').textContent  = rndNum();
    document.getElementById('numFav').textContent  = rndNum();
    document.getElementById('numShare').textContent= rndNum();
    var who = pick(names);
    document.getElementById('who').textContent = '@' + who;
    document.getElementById('avatar').firstChild.textContent = who[0];
    document.getElementById('txt').textContent = pick(texts);
    document.getElementById('mq').textContent = '@' + who + ' ' + pick(musics);

    spin.style.display = 'block';
    pauseIco.style.display = 'none';
    vd.style.opacity = 0;
    bar.style.width = 0;
    vd.src = apis[idx % apis.length] + '&_t=' + Date.now();
    idx++;
    vd.load();
    var p = vd.play();
    if (p) p.catch(function(){ pauseIco.style.display = 'block'; spin.style.display = 'none'; });
  }

  vd.addEventListener('canplay', function(){ spin.style.display = 'none'; vd.style.opacity = 1; });
  vd.addEventListener('waiting', function(){ spin.style.display = 'block'; });
  vd.addEventListener('playing', function(){ spin.style.display = 'none'; pauseIco.style.display = 'none'; vd.style.opacity = 1; });
  vd.addEventListener('ended', load);
  vd.addEventListener('error', function(){ setTimeout(load, 400); });
  vd.addEventListener('timeupdate', function(){
    if (vd.duration) bar.style.width = (vd.currentTime / vd.duration * 100) + '%';
  });

  // ── 单击暂停/播放，双击飘心点赞 ──
  var lastTap = 0, tapTimer = 0;
  function heartAt(x, y){
    var h = document.createElement('div');
    h.className = 'heart'; h.textContent = '\u2764';
    h.style.left = (x - 37) + 'px'; h.style.top = (y - 48) + 'px';
    document.body.appendChild(h);
    setTimeout(function(){ h.remove(); }, 900);
    if (!liked){ liked = true; document.getElementById('btnLike').classList.add('liked'); }
  }
  function onTap(x, y){
    var now = Date.now();
    if (now - lastTap < 280){          // 双击：点赞飘心
      clearTimeout(tapTimer);
      lastTap = 0;
      heartAt(x, y);
      return;
    }
    lastTap = now;
    tapTimer = setTimeout(function(){   // 单击：暂停/播放
      if (vd.paused){ vd.play(); pauseIco.style.display = 'none'; }
      else { vd.pause(); pauseIco.style.display = 'block'; }
    }, 285);
  }

  // ── 手势：触摸上/下滑切换；滚轮切换（桌面/网页） ──
  var ty = 0, tx = 0, moved = false;
  document.addEventListener('touchstart', function(e){
    ty = e.touches[0].clientY; tx = e.touches[0].clientX; moved = false;
  }, {passive:true});
  document.addEventListener('touchmove', function(){ moved = true; }, {passive:true});
  document.addEventListener('touchend', function(e){
    var dy = e.changedTouches[0].clientY - ty;
    var dx = e.changedTouches[0].clientX - tx;
    if (Math.abs(dy) > 60 && Math.abs(dy) > Math.abs(dx)){
      load();                            // 上滑/下滑都切下一条（源为随机流）
    } else if (!moved){
      var t = e.target;
      if (!t.closest('.side') && !t.closest('.topbar')) onTap(tx, ty);
    }
  }, {passive:true});

  var wheelLock = 0;
  document.addEventListener('wheel', function(e){
    var now = Date.now();
    if (now - wheelLock < 700) return;
    if (Math.abs(e.deltaY) > 40){ wheelLock = now; load(); }
  }, {passive:true});

  // 鼠标单双击（桌面 WebView / 浏览器）
  document.addEventListener('click', function(e){
    if (e.target.closest('.side') || e.target.closest('.topbar')) return;
    onTap(e.clientX, e.clientY);
  });

  // ── 右侧操作 ──
  document.getElementById('btnLike').addEventListener('click', function(e){
    e.stopPropagation();
    liked = !liked;
    this.classList.toggle('liked', liked);
    if (liked) heartAt(window.innerWidth - 80, window.innerHeight - 300);
  });
  Array.prototype.forEach.call(document.querySelectorAll('.act'), function(el){
    if (el.id === 'btnLike') return;
    el.addEventListener('click', function(e){ e.stopPropagation(); showToast('演示模式：功能仅供展示'); });
  });
  document.getElementById('avatar').addEventListener('click', function(e){
    e.stopPropagation(); showToast('已关注');
    this.querySelector('.plus').style.display = 'none';
  });

  // ── 返回：iframe → postMessage；WebView → app://exit 导航 ──
  document.getElementById('btnBack').addEventListener('click', function(e){
    e.stopPropagation();
    try {
      if (window.parent !== window){ window.parent.postMessage('douyin-exit', '*'); return; }
    } catch(err){}
    window.location.href = 'app://exit';
  });

  load();
})();
</script>
</body>
</html>
""";
}
