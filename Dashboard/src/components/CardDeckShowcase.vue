<script setup>
import { ref, computed, watch, onUnmounted, nextTick } from "vue";

const props = defineProps({
  images: { type: Array, required: true },
  interval: { type: Number, default: 3000 },
  active: { type: Boolean, default: false },
});

// Ensure at least 3 images for the deck effect
const deck = computed(() => {
  const imgs = props.images;
  if (imgs.length >= 3) return imgs;
  if (imgs.length === 0) return [];
  const filled = [];
  for (let i = 0; i < 3; i++) {
    filled.push(imgs[i % imgs.length]);
  }
  return filled;
});

const frontIndex = ref(0);
const leaving = ref(false);
const resetIndex = ref(null);
let timer = null;
let cycleTimeout = null;

function posClass(i) {
  const p = (i - frontIndex.value + 3) % 3;
  if (p === 0) return "card-front";
  if (p === 1) return "card-mid";
  return "card-back";
}

function cycle() {
  if (leaving.value) return;
  leaving.value = true;
  cycleTimeout = setTimeout(async () => {
    frontIndex.value = (frontIndex.value + 1) % 3;
    // The card that just left (old front) snaps instantly to the back
    resetIndex.value = (frontIndex.value + 2) % 3;
    leaving.value = false;
    await nextTick();
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        resetIndex.value = null;
      });
    });
  }, 650);
}

function start() {
  if (timer) clearInterval(timer);
  timer = setInterval(cycle, props.interval);
}

function stop() {
  if (timer) {
    clearInterval(timer);
    timer = null;
  }
  if (cycleTimeout) {
    clearTimeout(cycleTimeout);
    cycleTimeout = null;
  }
}

watch(
  () => props.active,
  (val) => {
    if (val) start();
    else stop();
  },
  { immediate: true },
);

onUnmounted(stop);
</script>

<template>
  <div class="deck">
    <div
      v-for="i in 3"
      :key="i"
      class="card"
      :class="{
        [posClass(i - 1)]: true,
        'is-leaving': leaving && i - 1 === frontIndex,
        'no-transition': resetIndex === i - 1,
      }"
    >
      <img :src="deck[i - 1]" :alt="`Showcase ${i}`" />
    </div>
  </div>
</template>

<style scoped>
.deck {
  position: relative;
  width: 100%;
  max-width: 640px;
  aspect-ratio: 3/2;
  margin: 0 auto;
}

.card {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  transition:
    transform 0.6s cubic-bezier(0.4, 0, 0.2, 1),
    opacity 0.6s cubic-bezier(0.4, 0, 0.2, 1),
    z-index 0s step-end;
}

.card.no-transition {
  transition: none !important;
}

.card img {
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
  object-position: center;
  border-radius: 14px;
  box-shadow: 0 12px 40px rgba(0, 0, 0, 0.5);
}

.card-front {
  transform: translate(0, 0) scale(1) rotate(-1deg);
  z-index: 3;
}

.card-mid {
  transform: translate(-12px, 12px) scale(0.96) rotate(1.5deg);
  z-index: 2;
  opacity: 0.88;
}

.card-back {
  transform: translate(-24px, 24px) scale(0.92) rotate(-1.5deg);
  z-index: 1;
  opacity: 0.7;
}

.is-leaving {
  transform: translate(75%, -35px) scale(1.02) rotate(7deg);
  opacity: 0;
  z-index: 10;
}
</style>
