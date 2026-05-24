<script setup>
const props = defineProps({
  images: { type: Array, required: true },
});

const stack = props.images.slice(0, 3);
</script>

<template>
  <div class="showcase-wrapper">
    <div
      v-for="(img, i) in stack"
      :key="i"
      class="showcase-img"
      :style="{ '--delay': `${i * 0.6}s` }"
    >
      <div class="img-frame">
        <img :src="img" :alt="`Showcase ${i + 1}`" />
      </div>
      <div class="img-glow"></div>
    </div>
  </div>
</template>

<style scoped>
.showcase-wrapper {
  perspective: 1200px;
  position: relative;
  width: 100%;
  max-width: 720px;
  aspect-ratio: 1 / 1;
  display: flex;
  align-items: center;
  justify-content: center;
}

.showcase-img {
  position: absolute;
  display: flex;
  flex-direction: column;
  align-items: center;
  transition: transform 0.6s cubic-bezier(0.4, 0, 0.2, 1);
}

.img-frame {
  border-radius: 16px;
  overflow: hidden;
  box-shadow: 0 20px 50px rgba(0, 0, 0, 0.55);
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.img-frame img {
  display: block;
  max-width: 500px;
  max-height: 500px;
  width: auto;
  height: auto;
  object-fit: contain;
}

.img-glow {
  width: 70%;
  height: 20px;
  border-radius: 50%;
  margin-top: 12px;
  background: radial-gradient(
    ellipse at center,
    rgba(var(--accent-rgb), 0.35) 0%,
    rgba(var(--accent-rgb), 0.08) 50%,
    transparent 80%
  );
  filter: blur(8px);
  animation: glow-pulse 4s ease-in-out infinite;
  animation-delay: var(--delay);
}

/* Stacked positions */
.showcase-img:nth-child(1) {
  transform: translate(150px, -60px) rotateY(22deg) rotateX(4deg) scale(1);
  z-index: 3;
  animation: float-card-1 6s ease-in-out infinite;
  animation-delay: var(--delay);
}

.showcase-img:nth-child(2) {
  transform: translate(50px, 0px) rotateY(22deg) rotateX(4deg) scale(1);
  z-index: 2;
  animation: float-card-2 6s ease-in-out infinite;
  animation-delay: var(--delay);
}

.showcase-img:nth-child(3) {
  transform: translate(-100px, -80px) rotateY(22deg) rotateX(4deg) scale(1);
  z-index: 1;
  animation: float-card-3 6s ease-in-out infinite;
  animation-delay: var(--delay);
}

@keyframes float-card-1 {
  0%,
  100% {
    transform: translate(150px, -60px) rotateY(22deg) rotateX(4deg) scale(1);
  }
  50% {
    transform: translate(150px, -40px) rotateY(22deg) rotateX(4deg) scale(1);
  }
}

@keyframes float-card-2 {
  0%,
  100% {
    transform: translate(50px, 0px) rotateY(22deg) rotateX(4deg) scale(1);
  }
  50% {
    transform: translate(50px, 20px) rotateY(22deg) rotateX(4deg) scale(1);
  }
}

@keyframes float-card-3 {
  0%,
  100% {
    transform: translate(-100px, -80px) rotateY(22deg) rotateX(4deg) scale(1);
  }
  50% {
    transform: translate(-100px, -60px) rotateY(22deg) rotateX(4deg) scale(1);
  }
}

@keyframes glow-pulse {
  0%,
  100% {
    opacity: 0.8;
    transform: scale(1);
  }
  50% {
    opacity: 1;
    transform: scale(1.15);
  }
}

@media (max-width: 960px) {
  .showcase-wrapper {
    max-width: 500px;
  }

  .img-frame img {
    max-width: 280px;
    max-height: 360px;
  }

  .showcase-img:nth-child(1) {
    transform: translate(40px, -15px) rotateY(-18deg) rotateX(3deg) scale(1);
  }
  .showcase-img:nth-child(2) {
    transform: translate(0px, 15px) rotateY(-18deg) rotateX(3deg) scale(0.92);
  }
  .showcase-img:nth-child(3) {
    transform: translate(-40px, 45px) rotateY(-18deg) rotateX(3deg) scale(0.84);
  }

  @keyframes float-card-1 {
    0%,
    100% {
      transform: translate(40px, -15px) rotateY(-18deg) rotateX(3deg) scale(1);
    }
    50% {
      transform: translate(40px, -35px) rotateY(-18deg) rotateX(3deg) scale(1);
    }
  }
  @keyframes float-card-2 {
    0%,
    100% {
      transform: translate(0px, 15px) rotateY(-18deg) rotateX(3deg) scale(0.92);
    }
    50% {
      transform: translate(0px, -5px) rotateY(-18deg) rotateX(3deg) scale(0.92);
    }
  }
  @keyframes float-card-3 {
    0%,
    100% {
      transform: translate(-40px, 45px) rotateY(-18deg) rotateX(3deg)
        scale(0.84);
    }
    50% {
      transform: translate(-40px, 25px) rotateY(-18deg) rotateX(3deg)
        scale(0.84);
    }
  }
}
</style>
