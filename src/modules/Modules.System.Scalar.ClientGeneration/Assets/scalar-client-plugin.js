var e = [], t = (e) => e.charCodeAt(0) === 111 && e.charCodeAt(1) === 110 && (e.charCodeAt(2) > 122 || e.charCodeAt(2) < 97), n = (e) => e.startsWith("onUpdate:"), r = Object.assign, i = Array.isArray, a = (e) => typeof e == "function", o = (e) => typeof e == "string", s = (e) => typeof e == "symbol", c = (e) => typeof e == "object" && !!e, l, u = () => l ||= typeof globalThis < "u" ? globalThis : typeof self < "u" ? self : typeof window < "u" ? window : typeof global < "u" ? global : {};
function d(e) {
	if (i(e)) {
		let t = {};
		for (let n = 0; n < e.length; n++) {
			let r = e[n], i = o(r) ? h(r) : d(r);
			if (i) for (let e in i) t[e] = i[e];
		}
		return t;
	}
	if (o(e) || c(e)) return e;
}
var f = /;(?![^(]*\))/g, p = /:([^]+)/, m = /"(?:[^"\\]|\\[^])*"|'(?:[^'\\]|\\[^])*'|\\[^]|\/\*[^]*?\*\//g;
function h(e) {
	let t = {};
	return e.replace(m, (e) => e.startsWith("/*") ? "" : e).split(f).forEach((e) => {
		if (e) {
			let n = e.split(p);
			n.length > 1 && (t[n[0].trim()] = n[1].trim());
		}
	}), t;
}
function g(e) {
	let t = "";
	if (o(e)) t = e;
	else if (i(e)) for (let n = 0; n < e.length; n++) {
		let r = g(e[n]);
		r && (t += r + " ");
	}
	else if (c(e)) for (let n in e) e[n] && (t += n + " ");
	return t.trim();
}
Array.prototype, new Set(/* @__PURE__ */ Object.getOwnPropertyNames(Symbol).filter((e) => e !== "arguments" && e !== "caller").map((e) => Symbol[e]).filter(s));
// @__NO_SIDE_EFFECTS__
function _(e) {
	return e ? !!e.__v_raw : !1;
}
// @__NO_SIDE_EFFECTS__
function v(e) {
	return e ? e.__v_isRef === !0 : !1;
}
var y = null, b = null, x = (e) => e.__isTeleport;
function S(e) {
	let t = e[0];
	if (e.length > 1) {
		for (let n of e) if (n.type !== N) {
			t = n;
			break;
		}
	}
	return t;
}
function C(e) {
	if (!E(e)) return x(e.type) && e.children ? S(e.children) : e;
	if (e.component) return e.component.subTree;
	let { shapeFlag: t, children: n } = e;
	if (n) {
		if (t & 16) return n[0];
		if (t & 32 && a(n.default)) return n.default();
	}
}
function w(e, t) {
	if (e.shapeFlag & 6 && e.component) {
		e.transition = t;
		let n = e.component.subTree;
		w(x(n.type) && C(n) || n, t);
	} else e.shapeFlag & 128 ? (e.ssContent.transition = t.clone(e.ssContent), e.ssFallback.transition = t.clone(e.ssFallback)) : e.transition = t;
}
// @__NO_SIDE_EFFECTS__
function T(e, t) {
	return a(e) ? /* @__PURE__ */ r({ name: e.name }, t, { setup: e }) : e;
}
u().requestIdleCallback, u().cancelIdleCallback;
var E = (e) => e.type.__isKeepAlive, D = /* @__PURE__ */ Symbol.for("v-ndc"), O = {}, k = (e) => Object.getPrototypeOf(e) === O, A = (e) => e.__isSuspense, j = /* @__PURE__ */ Symbol.for("v-fgt"), M = /* @__PURE__ */ Symbol.for("v-txt"), N = /* @__PURE__ */ Symbol.for("v-cmt"), P = [], F = null;
function I(e = !1) {
	P.push(F = e ? null : []);
}
function L() {
	P.pop(), F = P[P.length - 1] || null;
}
var R = 1;
function z(t) {
	return t.dynamicChildren = R > 0 ? F || e : null, L(), R > 0 && F && F.push(t), t;
}
function B(e, t, n, r, i, a) {
	return z(W(e, t, n, r, i, a, !0));
}
function V(e) {
	return e ? e.__v_isVNode === !0 : !1;
}
var H = ({ key: e }) => e ?? null, U = ({ ref: e, ref_key: t, ref_for: n }) => (typeof e == "number" && (e = "" + e), e == null ? null : o(e) || /* @__PURE__ */ v(e) || a(e) ? {
	i: y,
	r: e,
	k: t,
	f: !!n
} : e);
function W(e, t = null, n = null, r = 0, i = null, a = e === j ? 0 : 1, s = !1, c = !1) {
	let l = {
		__v_isVNode: !0,
		__v_skip: !0,
		type: e,
		props: t,
		key: t && H(t),
		ref: t && U(t),
		scopeId: b,
		slotScopeIds: null,
		children: n,
		component: null,
		suspense: null,
		ssContent: null,
		ssFallback: null,
		dirs: null,
		transition: null,
		el: null,
		anchor: null,
		target: null,
		targetStart: null,
		targetAnchor: null,
		staticCount: 0,
		shapeFlag: a,
		patchFlag: r,
		dynamicProps: i,
		dynamicChildren: null,
		appContext: null,
		ctx: y
	};
	return c ? (X(l, n), a & 128 && e.normalize(l)) : n && (l.shapeFlag |= o(n) ? 8 : 16), R > 0 && !s && F && (l.patchFlag > 0 || a & 6) && l.patchFlag !== 32 && F.push(l), l;
}
var G = K;
function K(e, t = null, n = null, s = 0, l = null, u = !1) {
	if ((!e || e === D) && (e = N), V(e)) {
		let r = J(e, t, !0);
		return n && X(r, n), R > 0 && !u && F && (r.shapeFlag & 6 ? F[F.indexOf(e)] = r : F.push(r)), r.patchFlag = -2, r;
	}
	if ($(e) && (e = e.__vccOpts), t) {
		t = q(t);
		let { class: e, style: n } = t;
		e && !o(e) && (t.class = g(e)), c(n) && (/* @__PURE__ */ _(n) && !i(n) && (n = r({}, n)), t.style = d(n));
	}
	let f = o(e) ? 1 : A(e) ? 128 : x(e) ? 64 : c(e) ? 4 : a(e) ? 2 : 0;
	return W(e, t, n, s, l, f, u, !0);
}
function q(e) {
	return e ? /* @__PURE__ */ _(e) || k(e) ? r({}, e) : e : null;
}
function J(e, t, n = !1, r = !1) {
	let { props: a, ref: o, patchFlag: s, children: c, transition: l } = e, u = t ? Z(a || {}, t) : a, d = {
		__v_isVNode: !0,
		__v_skip: !0,
		type: e.type,
		props: u,
		key: u && H(u),
		ref: t && t.ref ? n && o ? i(o) ? o.concat(U(t)) : [o, U(t)] : U(t) : o,
		scopeId: e.scopeId,
		slotScopeIds: e.slotScopeIds,
		children: c,
		target: e.target,
		targetStart: e.targetStart,
		targetAnchor: e.targetAnchor,
		staticCount: e.staticCount,
		shapeFlag: e.shapeFlag,
		patchFlag: t && e.type !== j ? s === -1 ? 16 : s | 16 : s,
		dynamicProps: e.dynamicProps,
		dynamicChildren: e.dynamicChildren,
		appContext: e.appContext,
		dirs: e.dirs,
		transition: l,
		component: e.component,
		suspense: e.suspense,
		ssContent: e.ssContent && J(e.ssContent),
		ssFallback: e.ssFallback && J(e.ssFallback),
		placeholder: e.placeholder,
		el: e.el,
		anchor: e.anchor,
		ctx: e.ctx,
		ce: e.ce,
		cacheIndex: e.cacheIndex
	};
	return l && r && w(d, l.clone(d)), d;
}
function Y(e = " ", t = 0) {
	return G(M, null, e, t);
}
function X(e, t) {
	let n = 0, { shapeFlag: r } = e;
	if (t == null) t = null;
	else if (i(t)) n = 16;
	else if (typeof t == "object") {
		if (r & 65) {
			let n = t.default;
			n && (n._c && (n._d = !1), X(e, n()), n._c && (n._d = !0));
			return;
		}
		n = 32, !t._ && !k(t) && (t._ctx = y);
	} else if (a(t)) {
		if (r & 65) {
			X(e, { default: t });
			return;
		}
		t = {
			default: t,
			_ctx: y
		}, n = 32;
	} else t = String(t), r & 64 ? (n = 16, t = [Y(t)]) : n = 8;
	e.children = t, e.shapeFlag |= n;
}
function Z(...e) {
	let r = {};
	for (let a = 0; a < e.length; a++) {
		let o = e[a];
		for (let e in o) if (e === "class") r.class !== o.class && (r.class = g([r.class, o.class]));
		else if (e === "style") r.style = d([r.style, o.style]);
		else if (t(e)) {
			let t = r[e], a = o[e];
			a && t !== a && !(i(t) && t.includes(a)) ? r[e] = t ? [].concat(t, a) : a : a == null && t == null && !n(e) && (r[e] = a);
		} else e !== "" && (r[e] = o[e]);
	}
	return r;
}
{
	let e = u(), t = (t, n) => {
		let r;
		return (r = e[t]) || (r = e[t] = []), r.push(n), (e) => {
			r.length > 1 ? r.forEach((t) => t(e)) : r[0](e);
		};
	};
	t("__VUE_INSTANCE_SETTERS__", (e) => e), t("__VUE_SSR_SETTERS__", (e) => Q = e);
}
var Q = !1;
function $(e) {
	return a(e) && "__vccOpts" in e;
}
//#endregion
//#region src/ClientGenerationButton.vue?vue&type=script&setup=true&lang.ts
var ee = { style: { "margin-top": "12px" } }, te = /* @__PURE__ */ T({
	__name: "ClientGenerationButton",
	props: { xClientGeneration: {} },
	setup(e) {
		let t = e;
		console.log("[Scalar.ClientGeneration] props:", t);
		function n() {
			let e = t.xClientGeneration;
			if (console.log("[Scalar.ClientGeneration] extension:", e), !e?.downloadUrl) {
				console.error("downloadUrl not found", e);
				return;
			}
			window.location.href = `${e.downloadUrl}?target=angular`;
		}
		return (e, t) => (I(), B("div", ee, [W("button", {
			type: "button",
			onClick: n,
			style: {
				display: "inline-flex",
				"align-items": "center",
				gap: "7px",
				"min-height": "34px",
				padding: "6px 12px",
				"border-radius": "6px",
				border: "1px solid var(--scalar-border-color)",
				background: "var(--scalar-background-2)",
				color: "var(--scalar-color-1)",
				"font-family": "inherit",
				"font-size": "13px",
				"font-weight": "500",
				cursor: "pointer"
			}
		}, " ↓ Download Angular Client ")]));
	}
}), ne = () => ({
	name: "scalar-client-generation",
	extensions: [{
		name: "x-client-generation",
		component: te
	}]
});
//#endregion
export { ne as default };
