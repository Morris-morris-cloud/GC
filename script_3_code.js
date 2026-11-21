(function () {
  "use strict";

  const STEP_COPY = [
    { title: "等高切割", description: "观察水平面与山体在同一海拔相交，形成连续闭合线。" },
    { title: "形成交线", description: "每个水平切面都会留下同高交线，多层交线共同描述山体起伏。" },
    { title: "投影成图", description: "把各高度交线垂直投影到同一平面，得到完整等高线地形图。" },
    { title: "疏密判坡", description: "等高线越密，坡度越陡；等高线越疏，坡度越缓。" }
  ];

  const DEFAULT_VIEW = {
    title: "实验流程",
    summary: "点击开始实验，在当前背景中观察等高切割与交线形成，再通过右侧图解理解垂直投影和疏密判坡。",
    features: ["同一条等高线上海拔相等", "相邻等高线高差保持一致"],
    hint: "点击“开始实验”，直接在当前画面播放形成交线视频"
  };

  const LANDFORMS = {
    peak: {
      name: "山峰",
      subtitle: "闭合等高线",
      image: ""<<IMG:png>>"",
      topImage: ""<<IMG:png>>"",
      alt: "山峰立体示意图，蓝色等高线围绕山体逐级闭合",
      topAlt: "山峰真实俯视图，多层近同心闭合等高线向中心收缩",
      summary: "山峰四周较低、中心突起；等高线闭合成环，海拔数值由外向内逐渐升高。",
      features: ["中心海拔高、四周海拔低", "闭合线越向内，数值越大", "若数值向内降低，则应判为洼地"],
      hint: "山峰：闭合曲线，数值向中心升高"
    },
    ridge: {
      name: "山脊",
      subtitle: "凸低为脊",
      image: ""<<IMG:png>>"",
      topImage: ""<<IMG:png>>"",
      alt: "山脊立体示意图，蓝色等高线沿凸出的脊线向低处弯曲",
      topAlt: "山脊真实俯视图，等高线沿狭长高地呈舌状延伸",
      summary: "等高线弯曲部分向海拔低处凸出，脊线常成为两侧水系的分水岭。",
      features: ["等高线凸向低值方向", "脊线由高处向低处延伸", "雨水沿脊线两侧分流"],
      hint: "山脊：等高线向低处凸出，凸低为脊"
    },
    valley: {
      name: "山谷",
      subtitle: "凸高为谷",
      image: ""<<IMG:png>>"",
      topImage: ""<<IMG:png>>"",
      alt: "山谷立体示意图，两座山体之间的谷地有河流穿过",
      topAlt: "山谷真实俯视图，两侧坡面夹持低槽，等高线形成 V 形凹入",
      summary: "等高线弯曲部分向海拔高处凸出，谷底低洼并容易汇水形成河流。",
      features: ["等高线凸向高值方向", "V 形尖端指向上游高处", "谷底常发育河流"],
      hint: "山谷：等高线向高处凸出，凸高为谷"
    },
    saddle: {
      name: "鞍部",
      subtitle: "两峰之间",
      image: ""<<IMG:png>>"",
      topImage: ""<<IMG:png>>"",
      alt: "鞍部立体示意图，两座山峰之间形成马鞍状低地",
      topAlt: "鞍部真实俯视图，两组峰顶闭合圈之间的等高线明显收腰",
      summary: "鞍部位于相邻两座山峰之间，是沿山脊方向相对低洼的过渡地带。",
      features: ["两组闭合等高线相邻", "中部线条收窄，形似马鞍", "常是翻越山岭的低垭口"],
      hint: "鞍部：两峰之间相对低洼，俯视轮廓呈收腰形"
    },
    cliff: {
      name: "陡崖",
      subtitle: "密集或重合",
      image: ""<<IMG:png>>"",
      topImage: ""<<IMG:png>>"",
      alt: "陡崖立体示意图，崖壁处蓝色等高线高度密集",
      topAlt: "陡崖真实俯视图，崖壁处大量等高线极密集并近似重合",
      summary: "陡崖坡面近直立，不同高程的等高线会高度密集，局部可投影到同一位置而重合。",
      features: ["密集表示陡坡，重合才是陡崖特征", "单位水平距离内高差很大", "n 条线重合时：(n−1)d ≤ ΔH < (n+1)d"],
      hint: "陡崖：等高线密集或重合，坡度极陡"
    }
  };

  const dom = {
    app: document.getElementById("app-shell"),
    experimentVideo: document.getElementById("experiment-video"),
    densityBackground: document.getElementById("density-background"),
    landformImage: document.getElementById("landform-image"),
    landformTopImage: document.getElementById("landform-top-image"),
    projectionDiagram: document.getElementById("projection-diagram"),
    densityDiagram: document.getElementById("density-diagram"),
    mediaLoading: document.getElementById("media-loading"),
    mediaLabelKicker: document.getElementById("media-label-kicker"),
    mediaLabelTitle: document.getElementById("media-label-title"),
    mediaLabelCopy: document.getElementById("media-label-copy"),
    progress: document.getElementById("video-progress-fill"),
    playButton: document.getElementById("play-button"),
    playIcon: document.getElementById("play-icon"),
    playLabel: document.getElementById("play-label"),
    resetButton: document.getElementById("reset-button"),
    fullscreenButton: document.getElementById("fullscreen-button"),
    fullscreenLabel: document.getElementById("fullscreen-label"),
    workflowOpen: document.getElementById("workflow-open"),
    flowSteps: Array.from(document.querySelectorAll(".flow-step[data-step]")),
    flowCount: document.getElementById("flow-count"),
    flowLineFill: document.getElementById("flow-line-fill"),
    taskTitle: document.getElementById("task-title"),
    taskDescription: document.getElementById("task-description"),
    terrainTabs: Array.from(document.querySelectorAll(".terrain-tab[data-landform]")),
    returnExperiment: document.getElementById("return-experiment"),
    observationHeading: document.getElementById("observation-heading"),
    observationSummary: document.getElementById("observation-summary"),
    featureList: document.getElementById("feature-list"),
    topViewToggle: document.getElementById("top-view-toggle"),
    topViewToggleLabel: document.getElementById("top-view-toggle-label"),
    topViewToggleHint: document.getElementById("top-view-toggle-hint"),
    topViewThumbnail: document.getElementById("top-view-thumbnail"),
    hintText: document.getElementById("hint-text"),
    controlPanel: document.querySelector(".control-panel"),
    panelClose: document.getElementById("panel-close"),
    panelOpen: document.getElementById("panel-open"),
    toast: document.getElementById("toast")
  };

  const state = {
    selectedLandform: null,
    pendingLandform: null,
    viewMode: "perspective",
    step: 0,
    imageToken: 0,
    scanTimer: 0,
    annotationTimer: 0,
    toastTimer: 0,
    pendingStep: null,
    workflowTimer: 0,
    workflowRunToken: 0,
    workflowAutoPlaying: false
  };

  const ICONS = {
    play: '<svg viewBox="0 0 24 24"><path d="m9 6 9 6-9 6V6Z"/></svg>',
    pause: '<svg viewBox="0 0 24 24"><path d="M7 6h4v12H7V6Zm6 0h4v12h-4V6Z"/></svg>'
  };

  function showToast(message) {
    window.clearTimeout(state.toastTimer);
    dom.toast.textContent = message;
    dom.toast.classList.add("is-visible");
    state.toastTimer = window.setTimeout(function () {
      dom.toast.classList.remove("is-visible");
    }, 2600);
  }

  function hideLoading() {
    dom.mediaLoading.classList.add("is-hidden");
  }

  function cancelWorkflowSequence() {
    window.clearTimeout(state.workflowTimer);
    state.workflowTimer = 0;
    state.workflowRunToken += 1;
    state.workflowAutoPlaying = false;
  }

  function setWorkflowDiagram(element, active) {
    element.toggleAttribute("hidden", !active);
    element.classList.toggle("is-active", active);
  }

  function hideWorkflowVisuals() {
    setWorkflowDiagram(dom.projectionDiagram, false);
    setWorkflowDiagram(dom.densityDiagram, false);
    dom.densityBackground.classList.remove("is-visible");
    dom.app.classList.remove("has-workflow-visual");
  }

  function syncWorkflowPresentation(index) {
    const safeIndex = Math.max(0, Math.min(3, Number(index) || 0));
    const isExperiment = !state.selectedLandform;
    const showProjection = isExperiment && safeIndex === 2;
    const showDensity = isExperiment && safeIndex === 3;
    const densityWasVisible = dom.densityBackground.classList.contains("is-visible");
    setWorkflowDiagram(dom.projectionDiagram, showProjection);
    setWorkflowDiagram(dom.densityDiagram, showDensity);
    dom.densityBackground.classList.toggle("is-visible", showDensity);
    dom.app.classList.toggle("has-workflow-visual", showProjection || showDensity);
    if (isExperiment) dom.experimentVideo.classList.toggle("is-visible", !showDensity);
    if (showDensity && !densityWasVisible) triggerScan(dom.densityBackground);
  }

  function beginStaticWorkflowSequence() {
    cancelWorkflowSequence();
    const token = state.workflowRunToken;
    state.workflowAutoPlaying = true;
    renderStep(2);
    setPlayButton();
    dom.hintText.textContent = "自动演示：" + STEP_COPY[2].description;
    state.workflowTimer = window.setTimeout(function () {
      if (token !== state.workflowRunToken) return;
      renderStep(3);
      dom.hintText.textContent = "自动演示：" + STEP_COPY[3].description;
      state.workflowTimer = window.setTimeout(function () {
        if (token !== state.workflowRunToken) return;
        state.workflowTimer = 0;
        state.workflowAutoPlaying = false;
        setPlayButton();
        dom.hintText.textContent = "实验流程播放完成 · 可重新实验或选择典型地貌观察";
      }, 3600);
    }, 3200);
  }

  function renderStep(index) {
    const safeIndex = Math.max(0, Math.min(3, Number(index) || 0));
    state.step = safeIndex;
    const copy = STEP_COPY[safeIndex];
    dom.flowCount.textContent = (safeIndex + 1) + "/4";
    dom.flowLineFill.style.width = (safeIndex / 3) * 100 + "%";
    dom.taskTitle.textContent = copy.title;
    dom.taskDescription.textContent = copy.description;
    dom.flowSteps.forEach(function (button, buttonIndex) {
      button.classList.toggle("is-active", buttonIndex === safeIndex);
      button.classList.toggle("is-complete", buttonIndex < safeIndex);
      button.setAttribute("aria-current", buttonIndex === safeIndex ? "step" : "false");
    });
    syncWorkflowPresentation(safeIndex);
  }

  function renderObservation(view) {
    dom.observationHeading.textContent = view.name || view.title;
    dom.observationSummary.textContent = view.summary;
    dom.featureList.replaceChildren();
    view.features.forEach(function (feature) {
      const item = document.createElement("li");
      item.textContent = feature;
      dom.featureList.appendChild(item);
    });
  }

  function setPlayButton() {
    const duration = dom.experimentVideo.duration;
    const finished = dom.experimentVideo.ended || (Number.isFinite(duration) && duration > 0 && dom.experimentVideo.currentTime >= duration - 0.08);
    if (!dom.experimentVideo.paused) {
      dom.playLabel.textContent = "暂停实验";
      dom.playIcon.innerHTML = ICONS.pause;
      return;
    }
    dom.playIcon.innerHTML = ICONS.play;
    if (!state.selectedLandform && state.step >= 2) {
      dom.playLabel.textContent = "重新实验";
      return;
    }
    if (finished) dom.playLabel.textContent = "重新实验";
    else if (dom.experimentVideo.currentTime > 0.12) dom.playLabel.textContent = "继续实验";
    else dom.playLabel.textContent = "开始实验";
  }

  function updateMediaLabel(view) {
    if (view) {
      dom.mediaLabelKicker.textContent = state.viewMode === "top" ? "典型地貌 · 真实俯视图" : "典型地貌 · 立体示意";
      dom.mediaLabelTitle.textContent = view.name;
      dom.mediaLabelCopy.textContent = state.viewMode === "top" ? view.subtitle + "｜" + view.summary : view.summary;
      return;
    }
    dom.mediaLabelKicker.textContent = "实验流程";
    dom.mediaLabelTitle.textContent = "形成交线与垂直投影";
    dom.mediaLabelCopy.textContent = dom.experimentVideo.paused
      ? "点击“开始实验”，直接在当前画面播放形成交线视频"
      : STEP_COPY[state.step].description;
  }

  function updateViewToggle() {
    const view = LANDFORMS[state.selectedLandform];
    const hasLandform = Boolean(view);
    const isTopView = hasLandform && state.viewMode === "top";
    dom.topViewToggle.disabled = !hasLandform;
    dom.topViewToggle.classList.toggle("is-active", isTopView);
    dom.topViewToggle.setAttribute("aria-pressed", String(isTopView));
    dom.topViewToggleLabel.textContent = isTopView ? "立体图" : "俯视图";
    dom.topViewToggleHint.textContent = hasLandform ? "点击切换" : "选择地貌后切换";
    dom.topViewToggle.setAttribute(
      "aria-label",
      hasLandform ? "切换为" + (isTopView ? "立体图" : "俯视图") : "请先选择一种典型地貌"
    );

    if (!view) {
      dom.topViewThumbnail.hidden = true;
      dom.topViewThumbnail.removeAttribute("src");
      dom.topViewThumbnail.alt = "";
      return;
    }
    dom.topViewThumbnail.src = isTopView ? view.image : view.topImage;
    dom.topViewThumbnail.alt = isTopView ? view.name + "立体图预览" : view.name + "真实俯视图预览";
    dom.topViewThumbnail.hidden = false;
  }

  function triggerScan(targetImage) {
    window.clearTimeout(state.scanTimer);
    dom.app.classList.remove("is-scanning");
    dom.densityBackground.classList.remove("is-revealing");
    dom.landformImage.classList.remove("is-revealing");
    dom.landformTopImage.classList.remove("is-revealing");
    void dom.app.offsetWidth;
    dom.app.classList.add("is-scanning");
    (targetImage || (state.viewMode === "top" ? dom.landformTopImage : dom.landformImage)).classList.add("is-revealing");
    state.scanTimer = window.setTimeout(function () {
      dom.app.classList.remove("is-scanning");
      dom.densityBackground.classList.remove("is-revealing");
      dom.landformImage.classList.remove("is-revealing");
      dom.landformTopImage.classList.remove("is-revealing");
    }, 1220);
  }

  async function decodeImage(image) {
    if (typeof image.decode === "function") {
      await image.decode();
      return;
    }
    if (!image.complete) {
      await new Promise(function (resolve, reject) {
        image.addEventListener("load", resolve, { once: true });
        image.addEventListener("error", reject, { once: true });
      });
    }
  }

  function hideTerrainAnnotation() {
    window.clearTimeout(state.annotationTimer);
    dom.app.classList.remove("is-landform");
    if (window.TerrainAnnotation) window.TerrainAnnotation.hide();
  }

  function clearTerrainAnnotationNow() {
    window.clearTimeout(state.annotationTimer);
    state.annotationTimer = 0;
    if (window.TerrainAnnotation) window.TerrainAnnotation.hide(true);
  }

  function scheduleTerrainAnnotation(withScan) {
    window.clearTimeout(state.annotationTimer);
    dom.app.classList.add("is-landform");
    state.annotationTimer = window.setTimeout(function () {
      if (!state.selectedLandform || !window.TerrainAnnotation) return;
      window.TerrainAnnotation.show(state.selectedLandform, state.viewMode);
    }, withScan ? 420 : 0);
  }

  function applyLandformView(view, withScan) {
    const isTopView = state.viewMode === "top";
    hideWorkflowVisuals();
    dom.experimentVideo.classList.remove("is-visible");
    dom.landformImage.classList.toggle("is-visible", !isTopView);
    dom.landformTopImage.classList.toggle("is-visible", isTopView);
    updateMediaLabel(view);
    updateViewToggle();
    dom.hintText.textContent = isTopView
      ? view.name + "俯视图：" + view.hint.replace(view.name + "：", "")
      : view.hint;
    if (withScan) triggerScan();
    scheduleTerrainAnnotation(withScan);
  }

  function collapsePanelForCompactView() {
    if (!window.matchMedia("(max-width: 680px)").matches) return;
    dom.controlPanel.classList.add("is-collapsed");
    dom.panelOpen.hidden = false;
  }

  async function selectLandform(key) {
    const view = LANDFORMS[key];
    if (!view) return;
    const token = ++state.imageToken;
    cancelWorkflowSequence();
    state.pendingLandform = key;
    clearTerrainAnnotationNow();
    hideWorkflowVisuals();
    const nextMode = state.selectedLandform ? state.viewMode : "perspective";
    dom.experimentVideo.pause();
    const perspectiveProbe = new Image();
    const topProbe = new Image();
    perspectiveProbe.src = view.image;
    topProbe.src = view.topImage;

    try {
      await Promise.all([decodeImage(perspectiveProbe), decodeImage(topProbe)]);
    } catch (error) {
      if (token === state.imageToken) {
        state.pendingLandform = null;
        syncWorkflowPresentation(state.step);
        showToast("地貌图片加载失败，请检查对应资源文件");
      }
      return;
    }
    if (token !== state.imageToken) return;

    state.pendingLandform = null;
    state.selectedLandform = key;
    state.viewMode = nextMode;
    dom.landformImage.src = view.image;
    dom.landformImage.alt = view.alt;
    dom.landformTopImage.src = view.topImage;
    dom.landformTopImage.alt = view.topAlt;

    dom.terrainTabs.forEach(function (button) {
      const active = button.dataset.landform === key;
      button.classList.toggle("is-active", active);
      button.setAttribute("aria-pressed", String(active));
    });
    dom.returnExperiment.hidden = false;
    renderObservation(view);
    applyLandformView(view, true);
    collapsePanelForCompactView();
    setPlayButton();
  }

  async function toggleTopView() {
    const view = LANDFORMS[state.selectedLandform];
    if (!view) return;
    const previousMode = state.viewMode;
    const token = ++state.imageToken;
    state.pendingLandform = null;
    clearTerrainAnnotationNow();
    state.viewMode = previousMode === "top" ? "perspective" : "top";
    try {
      await decodeImage(state.viewMode === "top" ? dom.landformTopImage : dom.landformImage);
    } catch (error) {
      state.viewMode = previousMode;
      showToast("对应视图加载失败，请检查图片资源");
      return;
    }
    if (token !== state.imageToken) return;
    applyLandformView(view, true);
    collapsePanelForCompactView();
  }

  function showExperiment() {
    cancelWorkflowSequence();
    state.imageToken += 1;
    state.pendingLandform = null;
    state.selectedLandform = null;
    state.viewMode = "perspective";
    dom.app.classList.remove("is-scanning");
    hideWorkflowVisuals();
    hideTerrainAnnotation();
    dom.landformImage.classList.remove("is-visible", "is-revealing");
    dom.landformTopImage.classList.remove("is-visible", "is-revealing");
    dom.experimentVideo.classList.add("is-visible");
    dom.terrainTabs.forEach(function (button) {
      button.classList.remove("is-active");
      button.setAttribute("aria-pressed", "false");
    });
    dom.returnExperiment.hidden = true;
    renderObservation(DEFAULT_VIEW);
    updateViewToggle();
    updateMediaLabel(null);
  }

  function playVideo() {
    dom.experimentVideo.play().then(function () {
      setPlayButton();
      updateMediaLabel(null);
      dom.hintText.textContent = "实验播放中：" + STEP_COPY[state.step].description;
    }).catch(function (error) {
      if (error && error.name === "AbortError") return;
      showToast("视频暂时无法播放，请检查浏览器媒体权限");
    });
  }

  function toggleExperimentPlayback() {
    if (state.selectedLandform || state.pendingLandform || state.step >= 2) {
      showExperiment();
      if (Number.isFinite(dom.experimentVideo.duration) && dom.experimentVideo.duration > 0) {
        dom.experimentVideo.currentTime = 0;
      } else {
        state.pendingStep = 0;
      }
      renderStep(0);
      playVideo();
      return;
    }
    if (!dom.experimentVideo.paused) {
      dom.experimentVideo.pause();
      return;
    }
    const duration = dom.experimentVideo.duration;
    if (dom.experimentVideo.ended || (Number.isFinite(duration) && dom.experimentVideo.currentTime >= duration - 0.08)) {
      dom.experimentVideo.currentTime = 0;
      renderStep(0);
    }
    playVideo();
  }

  function playFromBeginning() {
    showExperiment();
    renderStep(0);
    if (Number.isFinite(dom.experimentVideo.duration) && dom.experimentVideo.duration > 0) {
      dom.experimentVideo.currentTime = 0;
    } else {
      state.pendingStep = 0;
    }
    playVideo();
  }

  function playStep(index) {
    const safeIndex = Math.max(0, Math.min(3, Number(index) || 0));
    showExperiment();
    renderStep(safeIndex);
    if (safeIndex <= 1) {
      if (Number.isFinite(dom.experimentVideo.duration) && dom.experimentVideo.duration > 0) {
        dom.experimentVideo.currentTime = safeIndex === 0 ? 0 : dom.experimentVideo.duration / 2;
      } else {
        state.pendingStep = safeIndex;
      }
      playVideo();
      return;
    }
    state.pendingStep = null;
    dom.experimentVideo.pause();
    setPlayButton();
    updateMediaLabel(null);
    dom.hintText.textContent = STEP_COPY[safeIndex].title + "：" + STEP_COPY[safeIndex].description;
  }

  function resetExperience() {
    state.pendingStep = null;
    dom.experimentVideo.pause();
    showExperiment();
    renderStep(0);
    if (Number.isFinite(dom.experimentVideo.duration) && dom.experimentVideo.duration > 0) {
      dom.experimentVideo.currentTime = 0;
    } else {
      state.pendingStep = 0;
    }
    dom.progress.style.width = "0%";
    dom.hintText.textContent = DEFAULT_VIEW.hint;
    setPlayButton();
    showToast("已重置到视频首帧");
  }

  function toggleFullscreen() {
    if (!document.fullscreenElement) {
      if (!dom.app.requestFullscreen) {
        showToast("当前浏览器不支持全屏");
        return;
      }
      dom.app.requestFullscreen().catch(function () { showToast("当前浏览器未允许进入全屏"); });
      return;
    }
    if (document.exitFullscreen) document.exitFullscreen().catch(function () {});
  }

  dom.experimentVideo.addEventListener("loadeddata", hideLoading, { once: true });
  dom.experimentVideo.addEventListener("canplay", hideLoading, { once: true });
  dom.experimentVideo.addEventListener("loadedmetadata", function () {
    if (state.pendingStep === null) return;
    const pending = state.pendingStep;
    state.pendingStep = null;
    const duration = dom.experimentVideo.duration;
    if (Number.isFinite(duration) && duration > 0) {
      dom.experimentVideo.currentTime = pending === 0 ? 0 : duration / 2;
      renderStep(pending);
    }
  });
  dom.experimentVideo.addEventListener("error", function () {
    hideLoading();
    showToast("形成交线视频加载失败，请确认“contour-formation.mp4”在当前目录");
  });
  dom.experimentVideo.addEventListener("play", function () {
    setPlayButton();
    updateMediaLabel(null);
    dom.hintText.textContent = "实验播放中：" + STEP_COPY[state.step].description;
  });
  dom.experimentVideo.addEventListener("pause", function () {
    setPlayButton();
    updateMediaLabel(null);
    if (!state.selectedLandform && state.step >= 2) {
      dom.hintText.textContent = STEP_COPY[state.step].title + "：" + STEP_COPY[state.step].description;
      return;
    }
    if (!dom.experimentVideo.ended && dom.experimentVideo.currentTime > 0.12 && !state.selectedLandform) {
      dom.hintText.textContent = "实验已暂停 · 点击“继续实验”恢复播放";
    }
  });
  dom.experimentVideo.addEventListener("ended", function () {
    if (
      state.selectedLandform ||
      state.pendingLandform ||
      state.step > 1 ||
      !dom.experimentVideo.ended
    ) return;
    dom.progress.style.width = "100%";
    beginStaticWorkflowSequence();
  });
  dom.experimentVideo.addEventListener("timeupdate", function () {
    const duration = dom.experimentVideo.duration;
    const ratio = Number.isFinite(duration) && duration > 0 ? dom.experimentVideo.currentTime / duration : 0;
    dom.progress.style.width = Math.max(0, Math.min(100, ratio * 100)) + "%";
    if (!state.selectedLandform && state.step === 0 && ratio >= 0.5) {
      renderStep(1);
      dom.hintText.textContent = "实验播放中：" + STEP_COPY[1].description;
    }
    if (!dom.experimentVideo.paused) updateMediaLabel(null);
  });

  dom.playButton.addEventListener("click", toggleExperimentPlayback);
  dom.workflowOpen.addEventListener("click", playFromBeginning);
  dom.resetButton.addEventListener("click", resetExperience);
  dom.returnExperiment.addEventListener("click", resetExperience);
  dom.topViewToggle.addEventListener("click", toggleTopView);
  dom.terrainTabs.forEach(function (button) {
    button.addEventListener("click", function () { selectLandform(button.dataset.landform); });
  });
  dom.flowSteps.forEach(function (button) {
    button.addEventListener("click", function () { playStep(Number(button.dataset.step)); });
  });

  dom.fullscreenButton.addEventListener("click", toggleFullscreen);
  document.addEventListener("fullscreenchange", function () {
    dom.fullscreenLabel.textContent = document.fullscreenElement ? "退出全屏" : "全屏";
  });
  dom.panelClose.addEventListener("click", function () {
    dom.controlPanel.classList.add("is-collapsed");
    dom.panelOpen.hidden = false;
  });
  dom.panelOpen.addEventListener("click", function () {
    dom.controlPanel.classList.remove("is-collapsed");
    dom.panelOpen.hidden = true;
  });

  document.addEventListener("keydown", function (event) {
    if (event.code === "Space" && !/BUTTON|INPUT|VIDEO/.test(document.activeElement.tagName)) {
      event.preventDefault();
      toggleExperimentPlayback();
    }
    if (/^[1-5]$/.test(event.key) && !event.metaKey && !event.ctrlKey && !event.altKey) {
      const keys = ["peak", "ridge", "valley", "saddle", "cliff"];
      selectLandform(keys[Number(event.key) - 1]);
    }
  });

  window.setTimeout(hideLoading, 7000);
  renderStep(0);
  renderObservation(DEFAULT_VIEW);
  updateViewToggle();
  updateMediaLabel(null);
  setPlayButton();

  window.__GEO_LAB_TEST__ = {
    getState: function () {
      return {
        selectedLandform: state.selectedLandform,
        pendingLandform: state.pendingLandform,
        viewMode: state.viewMode,
        step: state.step,
        videoPlaying: !dom.experimentVideo.paused,
        videoTime: dom.experimentVideo.currentTime,
        videoSource: dom.experimentVideo.currentSrc || dom.experimentVideo.src,
        workflowDiagram: !dom.projectionDiagram.hasAttribute("hidden") ? "projection" : !dom.densityDiagram.hasAttribute("hidden") ? "density" : null,
        workflowAutoPlaying: state.workflowAutoPlaying,
        densityBackground: dom.densityBackground.classList.contains("is-visible"),
        scanning: dom.app.classList.contains("is-scanning"),
        terrainAnnotation: window.TerrainAnnotation ? window.TerrainAnnotation.getState() : null,
        fullscreen: Boolean(document.fullscreenElement)
      };
    },
    selectLandform: selectLandform,
    toggleTopView: toggleTopView,
    showExperiment: showExperiment,
    playStep: playStep,
    reset: resetExperience
  };
})();
