// The page side of a uihtml (app-building plan, U9b), run before the page's own scripts. It makes
// the htmlComponent object R2025b hands a page's setup(htmlComponent): own properties Data,
// addEventListener, removeEventListener and sendEventToMATLAB, and nothing else; listeners called
// newest first; plain event objects - {Source, EventName, Data, PreviousData} for DataChanged and
// {Source, Data} for an event from MATLAB (probe u9b_bridge). setup runs after the window's load.
// Messages to the host go through chrome.webview.postMessage, and come back as web messages.
(() => {
  'use strict';
  if (window.__jgraphUiHtml || !window.chrome || !window.chrome.webview) {
    return;
  }

  const webview = window.chrome.webview;
  const post = message => webview.postMessage(message);
  const listeners = new Map();
  let data = [];

  const component = {};
  component.addEventListener = function (name, listener) {
    if (typeof listener !== 'function') {
      return;
    }
    const key = String(name);
    let list = listeners.get(key);
    if (!list) {
      list = [];
      listeners.set(key, list);
    }
    if (!list.includes(listener)) {
      list.push(listener);
    }
  };

  component.removeEventListener = function (name, listener) {
    const list = listeners.get(String(name));
    if (list) {
      const at = list.indexOf(listener);
      if (at >= 0) {
        list.splice(at, 1);
      }
    }
  };

  component.sendEventToMATLAB = function (name, value) {
    if (arguments.length === 0) {
      return; // R2025b's bridge cannot read an event with no name
    }
    if (value !== undefined) {
      structuredClone(value); // a function cannot be sent: DataCloneError, as R2025b's postMessage throws
    }
    const nameJson = JSON.stringify(name);
    if (nameJson === undefined) {
      return;
    }
    let json;
    try {
      json = JSON.stringify(value);
    } catch (e) {
      return; // a cycle: R2025b's MATLAB side drops it
    }
    post({ k: 'event', name: nameJson, json: json === undefined ? null : json });
  };

  // Data last, as R2025b's object lists it.
  Object.defineProperty(component, 'Data', {
    enumerable: true,
    get: () => data,
    set: value => {
      // JSON.stringify first: a value it cannot write (a cycle) throws here, as in R2025b. One it
      // writes as nothing (undefined, a function) is held, and MATLAB hears nothing.
      const json = JSON.stringify(value);
      data = value;
      if (json !== undefined) {
        post({ k: 'data', json: json });
      }
    },
  });

  const dispatch = (name, event) => {
    const list = listeners.get(name);
    if (!list) {
      return;
    }
    for (const listener of list.slice().reverse()) {
      try {
        listener.call(undefined, event);
      } catch (e) {
        console.error(e);
      }
    }
  };

  webview.addEventListener('message', e => {
    const m = e.data;
    if (!m || typeof m !== 'object') {
      return;
    }
    if (m.k === 'init') {
      data = JSON.parse(m.json);
      if (typeof window.setup === 'function') {
        try {
          window.setup(component);
        } catch (err) {
          console.error(err);
        }
      }
      post({ k: 'setup' });
    } else if (m.k === 'data') {
      const previous = data;
      data = JSON.parse(m.json);
      dispatch('DataChanged', { Source: component, EventName: 'DataChanged', Data: data, PreviousData: previous });
    } else if (m.k === 'event') {
      dispatch(String(m.name), { Source: component, Data: m.json === null ? undefined : JSON.parse(m.json) });
    }
  });

  Object.defineProperty(window, '__jgraphUiHtml', { value: true });
  const loaded = () => post({ k: 'loaded' });
  if (document.readyState === 'complete') {
    loaded();
  } else {
    window.addEventListener('load', loaded);
  }
})();
