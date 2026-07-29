namespace AvaloniaKit.Resources;

// ══════════════════════════════════════════════════════════════════════════════
//  DouyinHtml — 抖音短视频页面（一份 HTML，三端复用）
//  · 视频源：公开短视频 API（302 → CDN mp4），video 标签直连不受 CORS 限制
//  · 界面还原：全屏竖屏视频、右侧头像/点赞/评论/收藏/分享/唱片、底部作者与
//    文案、音乐跑马灯、底部细进度条、加载转圈
//  · 图像：博主/评论头像用 qlogo.cn（随机 QQ 号→真实头像，国内直连），
//    主页作品预览图用 picsum.photos/seed（永不 404）；加载失败自动回退
//    字母/渐变占位，不依赖网络也不留白
//  · 交互：上滑/下滑（触摸+滚轮）切换视频、单击暂停/播放、双击飘红心点赞
//  · 返回：由宿主 Avalonia 标题栏的统一返回按钮处理（覆盖层从标题栏下方开始，
//    HTML 内不再自绘返回按钮）
//  · 关注/推荐 Tab 由宿主 Avalonia 标题栏承载（HTML 不再自绘 Tab），切 Tab 时
//    宿主以对应初始状态重建覆盖层（Build 注入 Tab + 关注列表）
//  · 关注：信息流头像“+”号 / 主页关注按钮 → sendHost 上报宿主持久化；
//    关注 Tab 只刷已关注博主视频，顶部横条展示关注列表，空关注显引导页
//  · JS→宿主消息：app://xxx 导航拦截（Desktop/Android/iOS）或
//    postMessage 'douyin-msg:app://xxx'（Browser iframe）
// ══════════════════════════════════════════════════════════════════════════════
public static class DouyinHtml
{
    /// <summary>按初始状态生成页面（activeTab：0=关注 1=推荐；followsJson：[{"n":"名","a":"头像URL"},…]）</summary>
    public static string Build(int activeTab, string followsJson)
        => Template.Replace("__INIT_TAB__", activeTab.ToString())
                   .Replace("__INIT_FOLLOWS__", followsJson);

    private const string Template = """
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

  /* 关注 Tab：顶部关注博主横条（Tab 本体在宿主 Avalonia 标题栏） */
  .fstrip { position:fixed; top:0; left:0; right:0; z-index:30; display:none;
            gap:14px; padding:10px 14px 8px; overflow-x:auto;
            background:linear-gradient(#00000088,transparent); }
  .fstrip .fs { flex:none; display:flex; flex-direction:column; align-items:center;
                gap:4px; width:56px; }
  .fstrip .fa { width:44px; height:44px; border-radius:50%; border:2px solid #ffffff88;
                position:relative; overflow:hidden; color:#fff; font-weight:700;
                display:flex; align-items:center; justify-content:center;
                background:linear-gradient(135deg,#7F7FD5,#86A8E7,#91EAE4); }
  .fstrip .fa img { position:absolute; inset:0; width:100%; height:100%; object-fit:cover; }
  .fstrip .fn { font-size:11px; color:#ffffffcc; max-width:56px; overflow:hidden;
                text-overflow:ellipsis; white-space:nowrap; }
  /* 关注 Tab 空状态引导 */
  .fempty { position:fixed; inset:0; z-index:15; display:none; flex-direction:column;
            align-items:center; justify-content:center; gap:12px; color:#fff; }
  .fempty .sub { color:#ffffff80; font-size:13px; }

  /* 右侧操作栏 */
  .side { position:fixed; right:10px; bottom:120px; z-index:20;
          display:flex; flex-direction:column; align-items:center; gap:20px; }
  .avatar { width:50px; height:50px; border-radius:50%; border:2px solid #fff;
            background:linear-gradient(135deg,#7F7FD5,#86A8E7,#91EAE4);
            display:flex; align-items:center; justify-content:center;
            color:#fff; font-size:20px; font-weight:700; position:relative; }
  /* ★ 真实头像图：盖在字母占位上，加载失败隐藏露出占位 */
  .avatar img { position:absolute; inset:0; width:100%; height:100%;
                border-radius:50%; object-fit:cover; }
  .avatar .plus { position:absolute; bottom:-9px; left:50%; transform:translateX(-50%);
                  width:19px; height:19px; border-radius:50%; background:#FE2C55;
                  color:#fff; font-size:14px; line-height:19px; text-align:center; z-index:2; }
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

  /* ══ 评论弹层 ══ */
  .mask { position:fixed; inset:0; z-index:40; background:#00000080;
          opacity:0; pointer-events:none; transition:opacity .22s; }
  .mask.on { opacity:1; pointer-events:auto; }
  .sheet { position:fixed; left:0; right:0; bottom:0; height:66%; z-index:41;
           background:#161823; border-radius:16px 16px 0 0;
           transform:translateY(105%); transition:transform .28s cubic-bezier(.2,.8,.3,1);
           display:flex; flex-direction:column; }
  .sheet.on { transform:translateY(0); }
  .sheet-hd { position:relative; padding:14px 0 10px; text-align:center;
              color:#ffffffd0; font-size:13px; flex:none; }
  .sheet-x { position:absolute; right:6px; top:6px; width:36px; height:36px;
             display:flex; align-items:center; justify-content:center;
             color:#ffffff90; font-size:16px; }
  .sheet-list { flex:1; overflow-y:auto; padding:2px 14px 12px;
                -webkit-overflow-scrolling:touch; }
  .c-item { display:flex; gap:10px; padding:10px 0; }
  .c-ava { width:36px; height:36px; border-radius:50%; flex:none;
           display:flex; align-items:center; justify-content:center;
           color:#fff; font-size:15px; font-weight:700;
           position:relative; overflow:hidden; }
  .c-ava img { position:absolute; inset:0; width:100%; height:100%; object-fit:cover; }
  .c-body { flex:1; min-width:0; }
  .c-name { font-size:13px; color:#ffffff80; }
  .c-text { font-size:14.5px; color:#fff; line-height:1.45; margin-top:3px; }
  .c-meta { font-size:12px; color:#ffffff59; margin-top:4px; }
  .c-like { flex:none; text-align:center; color:#ffffff73; font-size:12px;
            padding-top:4px; min-width:30px; }
  .c-like .h { font-size:16px; display:block; }
  .c-like.on { color:#FE2C55; }
  .sheet-input { flex:none; display:flex; align-items:center; gap:10px;
                 padding:10px 14px calc(12px + env(safe-area-inset-bottom));
                 border-top:1px solid #ffffff14; }
  .fake-in { flex:1; background:#ffffff14; color:#ffffff60; font-size:14px;
             border-radius:18px; padding:9px 14px; }
  .sheet-input span { font-size:20px; color:#ffffffb0; }

  /* ══ 博主主页 ══ */
  .profile { position:fixed; inset:0; z-index:50; background:#121420;
             transform:translateX(102%); transition:transform .26s cubic-bezier(.2,.8,.3,1);
             overflow-y:auto; -webkit-overflow-scrolling:touch; }
  .profile.on { transform:translateX(0); }
  .p-cover { height:150px; background:linear-gradient(120deg,#2b2f4a,#4a2b3d,#1c2b3a);
             background-size:200% 200%; animation:flow 8s ease infinite; }
  @keyframes flow { 0%{background-position:0% 50%} 50%{background-position:100% 50%}
                    100%{background-position:0% 50%} }
  .p-head { padding:0 16px 14px; margin-top:-38px; }
  .p-avatar { width:76px; height:76px; border-radius:50%; border:3px solid #121420;
              display:flex; align-items:center; justify-content:center;
              color:#fff; font-size:30px; font-weight:700; position:relative;
              background:linear-gradient(135deg,#7F7FD5,#86A8E7,#91EAE4); }
  .p-avatar img { position:absolute; inset:0; width:100%; height:100%;
                  border-radius:50%; object-fit:cover; }
  .p-name { margin-top:10px; color:#fff; font-size:20px; font-weight:700; }
  .p-id { margin-top:4px; color:#ffffff66; font-size:12.5px; }
  .p-stats { margin-top:12px; color:#ffffff80; font-size:13px;
             display:flex; gap:18px; }
  .p-stats b { color:#fff; font-size:16px; margin-right:4px; }
  .p-bio { margin-top:10px; color:#ffffffcc; font-size:13.5px; line-height:1.5; }
  .p-btn { margin-top:14px; background:#FE2C55; color:#fff; text-align:center;
           font-size:15px; font-weight:600; border-radius:6px; padding:11px 0; }
  .p-btn.done { background:#ffffff1f; color:#ffffffb3; }
  .p-tabs { display:flex; margin-top:6px; border-bottom:1px solid #ffffff14; }
  .p-tabs span { flex:1; text-align:center; padding:11px 0; font-size:14px;
                 color:#ffffff66; }
  .p-tabs .on { color:#fff; font-weight:600; position:relative; }
  .p-tabs .on::after { content:''; position:absolute; left:50%; bottom:0;
                       transform:translateX(-50%); width:32px; height:2.5px;
                       background:#FACE15; border-radius:2px; }
  .p-grid { display:grid; grid-template-columns:repeat(3,1fr); gap:2px; padding:2px; }
  .p-cell { position:relative; aspect-ratio:3/4; min-height:140px; display:flex;
            align-items:flex-end; padding:6px; overflow:hidden; }
  .p-cell img { position:absolute; inset:0; width:100%; height:100%; object-fit:cover; }
  .p-cell .pv { position:relative; z-index:1; color:#fff; font-size:12px;
                text-shadow:0 1px 3px #000a; }
</style>
</head>
<body>
<div id="stage">
  <video id="vd" playsinline webkit-playsinline preload="auto"></video>
</div>

<div class="fstrip" id="fstrip"></div>
<div class="fempty" id="fempty">
  <div style="font-size:44px">&#129309;</div>
  <div style="font-size:16px;font-weight:600">还没有关注的人</div>
  <div class="sub">去「推荐」刷视频，点头像上的 + 关注博主吧</div>
</div>

<div class="side">
  <div class="avatar" id="avatar"><span id="avaTxt">A</span><img id="avaImg" alt=""/><span class="plus" id="plusBtn">+</span></div>
  <div class="act" id="btnLike"><span class="ico">&#10084;</span><span class="num" id="numLike">12.3w</span></div>
  <div class="act" id="btnCmt"><span class="ico">&#128172;</span><span class="num" id="numCmt">8592</span></div>
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

<!-- 评论弹层 -->
<div class="mask" id="cmtMask"></div>
<div class="sheet" id="cmtSheet">
  <div class="sheet-hd"><span id="cmtCount">0条评论</span><span class="sheet-x" id="cmtClose">&#10005;</span></div>
  <div class="sheet-list" id="cmtList"></div>
  <div class="sheet-input"><div class="fake-in">善语结善缘，恶语伤人心…</div><span>@</span><span>&#128522;</span></div>
</div>

<!-- 博主主页（返回由宿主标题栏统一按钮承担：主页打开时先关主页，否则退出模块） -->
<div class="profile" id="profilePage">
  <div class="p-cover"></div>
  <div class="p-head">
    <div class="p-avatar" id="pAvatar"><span id="pAvaTxt">A</span><img id="pAvaImg" alt=""/></div>
    <div class="p-name" id="pName">@小可爱</div>
    <div class="p-id" id="pId">抖音号：dy10086</div>
    <div class="p-stats">
      <span><b id="pLikes">12.3w</b>获赞</span>
      <span><b id="pFollow">321</b>关注</span>
      <span><b id="pFans">8.8w</b>粉丝</span>
    </div>
    <div class="p-bio" id="pBio">分享生活中的美好瞬间 ✨ 商务合作请私信</div>
    <div class="p-btn" id="pFollowBtn">+ 关注</div>
  </div>
  <div class="p-tabs"><span class="on" id="pTabWorks">作品</span><span id="pTabLikes">喜欢</span></div>
  <div class="p-grid" id="pGrid"></div>
</div>

<script>
(function(){
  // ── 视频源池：公开短视频 API（302 → CDN mp4），带随机参数防缓存 ──
  var apis = [
    'https://api.yujn.cn/api/zzxjj.php?type=video',
    'https://api.yujn.cn/api/xjj.php?type=video',
    'https://api.yujn.cn/api/nvda.php?type=video',
    'https://api.yujn.cn/api/manhuay.php?type=video'
  ];
  var names  = ['小可爱','阿May','旅行日记','美食家阿伟','街拍先生','山海故事','奶茶不加冰','慢生活研究所',
                '是阿柚啊','午后阳光','城市漫游者','会飞的鱼','麦子熟了','一只柯基','海边的卡夫卡','鹿归林',
                '桃气少女','老张的日常','摄影师K','元气少年','云端漫步','小鹿乱撞','阿泽Vlog','甜筒不加料',
                '追风少年','夏天的风','木木的手账','大力出奇迹','阿七拍拍','橘子汽水','山野来信','阿浩爱运动',
                '小满','深夜食堂','会画画的猫','浪里个浪','阿蓝的相册','闲不住的多多','南方的冬','西瓜味的夏天',
                '阿满的厨房','行走的地图','小尾巴','喵星研究所','日落收藏家','阿哲','小城故事','爱睡觉的猪'];
  var texts  = ['记录美好生活的每一个瞬间 #日常 #治愈','这条视频拍了三个小时，值了！#vlog',
                '谁能拒绝这样的风景呢 #旅行 #风光','今日份快乐已送达 #开心 #日常碎片',
                '第一次尝试这样拍，效果意外的好 #创意','慢下来，生活其实很美 #治愈系 #慢生活',
                '这个BGM也太上头了吧 #音乐 #热门','周末的正确打开方式 #周末 #放松',
                '跟着我镜头看世界 #风景 #旅行日记','平凡的一天也值得被记录 #生活',
                '谁懂啊这也太治愈了 #治愈 #日常','学会了记得点赞收藏哦 #教程 #干货',
                '这一刻突然觉得很幸福 #vlog #生活碎片','阳光正好，微风不燥 #晴天 #出片',
                '拍了好久终于满意了 #摄影 #出片','生活需要一点仪式感 #日常 #氛围感',
                '今天也是元气满满的一天 #正能量','分享一个我最近很爱的地方 #探店 #宝藏',
                '不期而遇的美好 #随手拍','愿你我都被生活温柔以待 #治愈系',
                '这波操作我给满分 #高能 #热门','人间烟火气最抚凡人心 #生活 #美食',
                '收藏这条，周末就出发 #攻略','简单的快乐最动人 #日常vlog'];
  var musics = ['创作的原声 - 抖音热门BGM','夏天的风 - 温柔女声版','Sunny Day - Chill Beats',
                '人间烟火 - 治愈钢琴曲','热门卡点BGM - DJ版','晚风轻拂 - 民谣弹唱',
                '海边的旋律 - Lo-Fi','漫步云端 - 轻音乐','城市夜色 - Deep House',
                '温柔告白 - 吉他版','旅途 - 纯音乐','初夏 - 钢琴曲',
                '星空下 - 治愈系','慢摇时光 - Remix','清晨第一缕阳光 - BGM','热门神曲 - 卡点版'];

  var vd = document.getElementById('vd'), bar = document.getElementById('bar');
  var spin = document.getElementById('spin'), pauseIco = document.getElementById('pauseIco');
  var toast = document.getElementById('toast');
  var idx = 0, liked = false, toastTimer = 0;
  var curWho = '小可爱';   // 当前视频作者（评论/主页共用）

  // ── 宿主注入的初始状态：Tab（0=关注 1=推荐）与已关注列表 [{n,a}] ──
  var isFollowTab = __INIT_TAB__ === 0;
  var follows = __INIT_FOLLOWS__;
  var forcedAuthor = null;   // 点关注横条头像 → 下一条指定该博主

  function isFollowed(name){
    for (var i = 0; i < follows.length; i++) if (follows[i].n === name) return true;
    return false;
  }

  // ── JS→宿主消息：原生端 app:// 导航拦截；Browser iframe 用 postMessage ──
  function sendHost(url){
    if (window.parent !== window){
      try { window.parent.postMessage('douyin-msg:' + url, '*'); } catch(e){}
    } else {
      location.href = url;
    }
  }

  // ── 覆盖层状态：评论/主页打开时屏蔽切视频与播放切换手势 ──
  function overlayOpen(){
    return document.getElementById('cmtSheet').classList.contains('on') ||
           document.getElementById('profilePage').classList.contains('on');
  }

  function rndNum(){ var n = Math.random()*30; return n>10 ? n.toFixed(1)+'w' : Math.floor(n*9000+500)+''; }
  function pick(a){ return a[Math.floor(Math.random()*a.length)]; }

  // ★ 真实头像：随机 QQ 号的 qlogo 头像（国内 CDN 直连，失败时回退字母占位）
  function qqAvatar(){
    return 'https://q' + (1 + Math.floor(Math.random()*4)) +
           '.qlogo.cn/g?b=qq&nk=' + Math.floor(1e8 + Math.random()*2.4e9) + '&s=100';
  }
  var curAvatar = '';   // 当前视频博主头像 URL（主页复用，与信息流一致）

  // ★ 不与上一次重复的随机选取（避免“同一博主连着刷”的重复感）
  function pickNoRepeat(a, last){
    if (a.length < 2) return a[0];
    var v; do { v = pick(a); } while (v === last);
    return v;
  }
  var _lastApi = '';
  function nextApi(){
    var u = pickNoRepeat(apis, _lastApi);
    _lastApi = u;
    return u;
  }

  function showToast(t){
    toast.textContent = t; toast.style.opacity = 1;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function(){ toast.style.opacity = 0; }, 1300);
  }

  // ── 关注 Tab 空状态：隐藏信息流控件并停掉视频 ──
  function showEmpty(on){
    document.getElementById('fempty').style.display = on ? 'flex' : 'none';
    document.querySelector('.side').style.display = on ? 'none' : 'flex';
    document.querySelector('.info').style.display = on ? 'none' : 'block';
    document.querySelector('.prog').style.display = on ? 'none' : 'block';
    if (on){
      try { vd.pause(); vd.removeAttribute('src'); vd.load(); } catch(e){}
      spin.style.display = 'none'; pauseIco.style.display = 'none';
    }
  }

  // 关注流选人：优先横条点选的博主，否则随机（避免连刷同一人）
  function pickFollow(){
    if (forcedAuthor){ var f = forcedAuthor; forcedAuthor = null; return f; }
    if (follows.length === 1) return follows[0];
    var v; do { v = follows[Math.floor(Math.random()*follows.length)]; } while (v.n === curWho);
    return v;
  }

  // ── 换一条视频（随机 API + 随机文案/数字；关注 Tab 只刷已关注博主） ──
  function load(){
    if (isFollowTab && follows.length === 0){ showEmpty(true); return; }
    showEmpty(false);
    liked = false;
    var like = document.getElementById('btnLike');
    like.classList.remove('liked');
    document.getElementById('numLike').textContent = rndNum();
    document.getElementById('numCmt').textContent  = rndNum();
    document.getElementById('numFav').textContent  = rndNum();
    document.getElementById('numShare').textContent= rndNum();
    if (isFollowTab){
      var f = pickFollow();
      curWho = f.n;
      curAvatar = f.a || qqAvatar();   // 关注流用收藏的头像，与关注时一致
    } else {
      curWho = pickNoRepeat(names, curWho);
      curAvatar = qqAvatar();
    }
    document.getElementById('who').textContent = '@' + curWho;
    // ★ 换人同步换头像：先露字母占位，图片加载成功后盖上（重置换号重试计数）
    document.getElementById('avaTxt').textContent = curWho[0];
    avaRetry = 0;
    var avaImg = document.getElementById('avaImg');
    avaImg.style.display = 'block';
    avaImg.src = curAvatar;
    syncFollowUi();
    document.getElementById('txt').textContent = pick(texts);
    document.getElementById('mq').textContent = '@' + curWho + ' ' + pick(musics);

    spin.style.display = 'block';
    pauseIco.style.display = 'none';
    vd.style.opacity = 0;
    bar.style.width = 0;
    vd.src = nextApi() + '&_t=' + Date.now() + Math.floor(Math.random()*1000);
    vd.load();
    var p = vd.play();
    if (p) p.catch(function(){ pauseIco.style.display = 'block'; spin.style.display = 'none'; });
  }

  vd.addEventListener('canplay', function(){ spin.style.display = 'none'; vd.style.opacity = 1; });
  // ★ 随机 QQ 头像可能不存在：失败自动换号重试，多次仍失败才回退字母占位
  var avaRetry = 0;
  document.getElementById('avaImg').addEventListener('error', function(){
    if (avaRetry < 2){ avaRetry++; curAvatar = qqAvatar(); this.src = curAvatar; }
    else this.style.display = 'none';
  });
  document.getElementById('pAvaImg').addEventListener('error', function(){
    if (this.getAttribute('data-retry') !== '1'){
      this.setAttribute('data-retry', '1');
      curAvatar = qqAvatar();
      this.src = curAvatar;
    } else this.style.display = 'none';
  });
  vd.addEventListener('waiting', function(){ spin.style.display = 'block'; });
  vd.addEventListener('playing', function(){ spin.style.display = 'none'; pauseIco.style.display = 'none'; vd.style.opacity = 1; });
  vd.addEventListener('ended', load);
  vd.addEventListener('error', function(){ setTimeout(load, 400); });
  vd.addEventListener('timeupdate', function(){
    if (vd.duration) bar.style.width = (vd.currentTime / vd.duration * 100) + '%';
  });

  // ── 单击立即暂停/播放（零延迟跟手），双击飘心点赞 ──
  var lastTap = 0;
  function heartAt(x, y){
    var h = document.createElement('div');
    h.className = 'heart'; h.textContent = '\u2764';
    h.style.left = (x - 37) + 'px'; h.style.top = (y - 48) + 'px';
    document.body.appendChild(h);
    setTimeout(function(){ h.remove(); }, 900);
    if (!liked){ liked = true; document.getElementById('btnLike').classList.add('liked'); }
  }
  function togglePlay(){
    if (vd.paused){ vd.play(); pauseIco.style.display = 'none'; }
    else { vd.pause(); pauseIco.style.display = 'block'; }
  }
  function onTap(x, y){
    var now = Date.now();
    if (now - lastTap < 300){          // 双击：点赞飘心，并抵消首击的暂停切换
      lastTap = 0;
      togglePlay();                    // 恢复首击前的播放状态
      heartAt(x, y);
      return;
    }
    lastTap = now;
    togglePlay();                      // ★ 单击立即生效，不再等双击判定延迟
  }

  // ── 手势：触摸上/下滑切换；滚轮切换（桌面/网页）──
  var ty = 0, tx = 0, moved = false, lastTouchTime = 0;
  document.addEventListener('touchstart', function(e){
    ty = e.touches[0].clientY; tx = e.touches[0].clientX; moved = false;
  }, {passive:true});
  document.addEventListener('touchmove', function(){ moved = true; }, {passive:true});
  document.addEventListener('touchend', function(e){
    lastTouchTime = Date.now();                    // ★ 屏蔽随后的合成 click，防重复触发
    if (overlayOpen()) return;                     // 弹层内滑动不切视频
    var dy = e.changedTouches[0].clientY - ty;
    var dx = e.changedTouches[0].clientX - tx;
    if (Math.abs(dy) > 60 && Math.abs(dy) > Math.abs(dx)){
      load();                            // 上滑/下滑都切下一条（源为随机流）
    } else if (!moved){
      var t = e.target;
      if (!t.closest('.side') && !t.closest('.fstrip') && !t.closest('.info') && !t.closest('.fempty')) onTap(tx, ty);
    }
  }, {passive:true});

  var wheelLock = 0;
  document.addEventListener('wheel', function(e){
    if (overlayOpen()) return;
    var now = Date.now();
    if (now - wheelLock < 700) return;
    if (Math.abs(e.deltaY) > 40){ wheelLock = now; load(); }
  }, {passive:true});

  // 鼠标单双击（桌面 WebView / 浏览器）
  document.addEventListener('click', function(e){
    // ★ 触屏设备：touchend 已处理，忽略浏览器补发的合成 click（否则暂停会被抵消）
    if (Date.now() - lastTouchTime < 600) return;
    if (overlayOpen()) return;
    if (e.target.closest('.side') || e.target.closest('.fstrip') ||
        e.target.closest('.info') || e.target.closest('.sheet') ||
        e.target.closest('.profile') || e.target.closest('.mask') ||
        e.target.closest('.fempty')) return;
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
    if (el.id === 'btnLike' || el.id === 'btnCmt') return;
    el.addEventListener('click', function(e){ e.stopPropagation(); showToast('演示模式：功能仅供展示'); });
  });
  // ── 关注：信息流头像 + 号（阻止冒泡，不进主页） ──
  document.getElementById('plusBtn').addEventListener('click', function(e){
    e.stopPropagation(); doFollow();
  });
  document.getElementById('avatar').addEventListener('click', function(e){
    e.stopPropagation(); openProfile();
  });
  document.getElementById('who').addEventListener('click', function(e){
    e.stopPropagation(); openProfile();
  });

  // ══ 评论弹层 ════════════════════════════════════════════════
  var cmtPool = [
    '这个必须赞！拍得太好了','前排占座，每天都来打卡','哈哈哈哈笑死我了',
    'BGM 叫什么？求同款','这是在哪里拍的呀，好想去','收藏了，周末就安排',
    '镜头感绝了，求教程','看完舍不得划走的程度','这才是生活该有的样子',
    '三连了，期待更新','每一帧都是壁纸','评论区有同好吗？报到！',
    '被治愈到了，谢谢分享','顶住，不要划走','这拍摄水平可以出道了'
  ];
  var cmtColors = ['#7F7FD5','#E8544C','#3D9F6E','#C97BC3','#5B8DEF','#D8A24B'];

  function buildComments(){
    var list = document.getElementById('cmtList');
    list.innerHTML = '';
    var n = 6 + Math.floor(Math.random() * 6);
    for (var i = 0; i < n; i++){
      var name = pick(names);
      var item = document.createElement('div');
      item.className = 'c-item';
      item.innerHTML =
        '<div class="c-ava" style="background:' + pick(cmtColors) + '">' + name[0] +
        '<img src="' + qqAvatar() + '" alt="" onerror="this.remove()"/></div>' +
        '<div class="c-body"><div class="c-name">' + name + '</div>' +
        '<div class="c-text">' + pick(cmtPool) + '</div>' +
        '<div class="c-meta">' + (1 + Math.floor(Math.random()*23)) + '小时前 · 回复</div></div>' +
        '<div class="c-like"><span class="h">&#9825;</span>' + Math.floor(Math.random()*9000+100) + '</div>';
      item.querySelector('.c-like').addEventListener('click', function(){
        this.classList.toggle('on');
        this.querySelector('.h').innerHTML = this.classList.contains('on') ? '&#10084;' : '&#9825;';
      });
      list.appendChild(item);
    }
    document.getElementById('cmtCount').textContent =
      document.getElementById('numCmt').textContent + '条评论';
  }
  function openComments(){
    buildComments();
    document.getElementById('cmtMask').classList.add('on');
    document.getElementById('cmtSheet').classList.add('on');
  }
  function closeComments(){
    document.getElementById('cmtMask').classList.remove('on');
    document.getElementById('cmtSheet').classList.remove('on');
  }
  document.getElementById('btnCmt').addEventListener('click', function(e){
    e.stopPropagation(); openComments();
  });
  document.getElementById('cmtClose').addEventListener('click', closeComments);
  document.getElementById('cmtMask').addEventListener('click', closeComments);

  // ══ 博主主页 ══════════════════════════════════════════════
  var bios = ['分享生活中的美好瞬间 ✨ 商务合作请私信','记录平凡日子里的小确幸 ☀︎',
              '爱拍视频的日常博主｜每周三六更新','镜头里的世界比想象更精彩 🎬',
              '一起发现好玩的事物吧｜合作请私信'];
  var gridColors = ['#2E3350','#503048','#2C4638','#4A3A2A','#31425C','#452F2F',
                    '#3B2F52','#2F4A4A','#523F2C'];

  function openProfile(){
    // ★ 主页头像与信息流头像保持同一张（失败自动换号重试一次后回退字母占位）
    document.getElementById('pAvaTxt').textContent = curWho[0];
    var pImg = document.getElementById('pAvaImg');
    pImg.removeAttribute('data-retry');
    pImg.style.display = 'block';
    pImg.src = curAvatar || qqAvatar();
    document.getElementById('pName').textContent = '@' + curWho;
    document.getElementById('pId').textContent =
      '抖音号：dy' + Math.floor(Math.random()*90000000+10000000);
    document.getElementById('pLikes').textContent = rndNum();
    document.getElementById('pFollow').textContent = Math.floor(Math.random()*500+20);
    document.getElementById('pFans').textContent = rndNum();
    document.getElementById('pBio').textContent = pick(bios);
    syncFollowUi();   // 关注按钮反映真实关注状态
    buildGrid();
    document.getElementById('profilePage').classList.add('on');
    sendHost('app://profile?open=1&t=' + Date.now());   // ★ 宿主据此让标题栏返回先关主页
    vd.pause(); pauseIco.style.display = 'none';   // 进主页暂停播放
  }
  function closeProfile(){
    document.getElementById('profilePage').classList.remove('on');
    sendHost('app://profile?open=0&t=' + Date.now());
    if (vd.paused){ vd.play(); }                    // 返回续播
  }
  function buildGrid(){
    var grid = document.getElementById('pGrid');
    grid.innerHTML = '';
    var n = 9 + Math.floor(Math.random()*4);
    for (var i = 0; i < n; i++){
      var cell = document.createElement('div');
      cell.className = 'p-cell';
      cell.style.background =
        'linear-gradient(160deg,' + pick(gridColors) + ',' + pick(gridColors) + ')';
      // ★ 真实预览图：picsum seed 永不 404；加载失败移除露出渐变占位
      cell.innerHTML =
        '<img src="https://picsum.photos/seed/dy' + Math.floor(Math.random()*100000) +
        '/240/320" alt="" loading="lazy" onerror="this.remove()"/>' +
        '<span class="pv">&#9654; ' + rndNum() + '</span>';
      cell.addEventListener('click', function(){ closeProfile(); load(); });
      grid.appendChild(cell);
    }
  }
  document.getElementById('pFollowBtn').addEventListener('click', function(){
    if (isFollowed(curWho)) doUnfollow(curWho); else doFollow();
  });
  document.getElementById('pTabWorks').addEventListener('click', function(){
    this.classList.add('on');
    document.getElementById('pTabLikes').classList.remove('on');
    buildGrid();
  });
  document.getElementById('pTabLikes').addEventListener('click', function(){
    this.classList.add('on');
    document.getElementById('pTabWorks').classList.remove('on');
    buildGrid();
  });

  // ══ 关注：本地状态同步 + 上报宿主持久化 ══════════════════════
  function syncFollowUi(){
    var on = isFollowed(curWho);
    document.getElementById('plusBtn').style.display = on ? 'none' : 'block';
    var btn = document.getElementById('pFollowBtn');
    btn.classList.toggle('done', on);
    btn.textContent = on ? '已关注' : '+ 关注';
  }
  function doFollow(){
    if (isFollowed(curWho)) return;
    follows.push({ n: curWho, a: curAvatar });
    sendHost('app://follow?n=' + encodeURIComponent(curWho) +
             '&a=' + encodeURIComponent(curAvatar) + '&t=' + Date.now());
    showToast('关注成功');
    syncFollowUi(); renderStrip();
  }
  function doUnfollow(name){
    for (var i = follows.length - 1; i >= 0; i--)
      if (follows[i].n === name) follows.splice(i, 1);
    sendHost('app://unfollow?n=' + encodeURIComponent(name) + '&t=' + Date.now());
    showToast('已取消关注');
    syncFollowUi(); renderStrip();
    // 关注 Tab 取消最后一个关注 → 收起主页并显示空状态引导
    if (isFollowTab && follows.length === 0){
      document.getElementById('profilePage').classList.remove('on');
      sendHost('app://profile?open=0&t=' + Date.now());
      showEmpty(true);
    }
  }
  // 关注 Tab 顶部横条：展示全部已关注博主，点头像刷该博主的视频
  function renderStrip(){
    var strip = document.getElementById('fstrip');
    strip.style.display = (isFollowTab && follows.length) ? 'flex' : 'none';
    if (!isFollowTab) return;
    strip.innerHTML = '';
    follows.forEach(function(f){
      var d = document.createElement('div');
      d.className = 'fs';
      d.innerHTML = '<div class="fa">' + f.n[0] +
        (f.a ? '<img src="' + f.a + '" alt="" onerror="this.remove()"/>' : '') +
        '</div><div class="fn">' + f.n + '</div>';
      d.addEventListener('click', function(e){
        e.stopPropagation(); forcedAuthor = f; load();
      });
      strip.appendChild(d);
    });
  }

  renderStrip();
  load();
})();
</script>
</body>
</html>
""";
}
