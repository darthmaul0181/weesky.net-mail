// React DOM picks the animation event names it listens for once, when it loads, and jsdom has no
// AnimationEvent: without one React listens for a prefixed name nothing fires. Imported first.
if (!('AnimationEvent' in window)) {
  Object.defineProperty(window, 'AnimationEvent', { value: class AnimationEvent extends Event {}, configurable: true })
}
