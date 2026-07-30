namespace AvaloniaKit.Resources;

// ── MapHtml 分部：页面骨架（DOM）──
//    顶部搜索卡（起终点+定位按钮+把手）/悬浮键/路线面板（把手）/导航视图（转向卡+
//    底栏+车速球+里程光柱）/语音包弹层/toast/加载/失败页；脚本在后续分部
public static partial class MapHtml
{
    private const string BodyPart = """
<body>
<div id="map"></div>

<div class="topbar" id="topbar">
  <div class="tb-body">
  <div class="route-row">
    <div class="io-wrap">
      <div class="io">
        <span class="dot start"></span>
        <input id="from" placeholder="选择起点">
        <button class="io-loc" data-t="from" title="定位到当前位置">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none">
            <circle cx="12" cy="12" r="3.1" fill="#1677ff"/>
            <circle cx="12" cy="12" r="7" stroke="#1677ff" stroke-width="1.6"/>
            <path d="M12 1.4V4.2M12 19.8V22.6M1.4 12H4.2M19.8 12H22.6" stroke="#1677ff" stroke-width="1.6" stroke-linecap="round"/>
          </svg>
        </button>
      </div>
      <div class="io">
        <span class="dot end"></span>
        <input id="to" placeholder="输入终点">
        <button class="io-loc" data-t="to" title="定位到当前位置">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none">
            <circle cx="12" cy="12" r="3.1" fill="#1677ff"/>
            <circle cx="12" cy="12" r="7" stroke="#1677ff" stroke-width="1.6"/>
            <path d="M12 1.4V4.2M12 19.8V22.6M1.4 12H4.2M19.8 12H22.6" stroke="#1677ff" stroke-width="1.6" stroke-linecap="round"/>
          </svg>
        </button>
      </div>
    </div>
    <button id="swap" title="交换起终点">⇅</button>
  </div>
  <div class="modes">
    <button data-m="drive" class="on">驾车</button>
    <button data-m="walk">步行</button>
    <button data-m="bus">公交</button>
    <button data-m="ride">骑行</button>
  </div>
  <button id="go">查路线</button>
  </div>
  <div class="grab" id="topGrab" title="收起/展开"><i></i></div>
</div>

<button class="voice-fab" id="voiceFab">🎙 <span id="voiceName">语音包</span></button>
<button class="loc-fab" id="loc" title="回到我的位置">
  <svg width="22" height="22" viewBox="0 0 24 24" fill="none">
    <circle cx="12" cy="12" r="3.3" fill="#1677ff"/>
    <circle cx="12" cy="12" r="7.6" stroke="#1677ff" stroke-width="1.8"/>
    <path d="M12 1V4.3M12 19.7V23M1 12H4.3M19.7 12H23" stroke="#1677ff" stroke-width="1.8" stroke-linecap="round"/>
  </svg>
</button>

<div class="plan-panel hidden" id="planPanel">
  <div class="grab" id="planGrab" title="收起/展开"><i></i></div>
  <div class="pp-body">
    <div class="rcards" id="routeCards"></div>
    <button class="start-nav" id="startNav">🧭 开始导航</button>
  </div>
</div>

<div class="nav-top hidden" id="navTop">
  <div class="turn-ico" id="turnIco">↑</div>
  <div class="turn-main" style="min-width:0;flex:1">
    <div class="turn-dist" id="turnDist"></div>
    <div class="turn-instr" id="turnInstr">开始导航</div>
  </div>
</div>
<div class="nav-bottom hidden" id="navBottom">
  <div class="nav-prog"><i id="navProgFill"></i></div>
  <div class="nav-info"><b id="navRemain">--</b><span id="navEta"></span></div>
  <button class="exit-nav" id="exitNav">退出</button>
</div>
<div class="speed-ball hidden" id="speedBall"><b id="speedVal">0</b><span>km/h</span></div>
<div class="nav-rail hidden" id="navRail">
  <div class="rail-track" id="railTrack"></div>
  <div class="rail-done" id="railDone"></div>
  <div class="rail-car" id="railCar"></div>
</div>

<div class="mask hidden" id="mask"></div>
<div class="sheet hidden" id="voiceSheet"><h3>选择语音包</h3><div id="packList"></div></div>

<div class="toast" id="toast"></div>
<div class="spin" id="spin"><div class="spin-c"></div><span>正在规划路线…</span></div>
<div class="fail hidden" id="fail">
  <div class="em">🛰️</div>
  <div class="tx">地图加载失败：请检查网络，或确认高德 Key 已正确配置（Web 端 JS API + 安全密钥，且域名白名单留空）。</div>
</div>

""";
}
