<script setup>
import GameTag from './GameTag.vue';

const props = defineProps({
  doc: {
    type: Object,
    required: true,
  },
});

const emit = defineEmits(['click']);
</script>

<template>
  <div class="doc-card" @click="emit('click', doc)">
    <div class="doc-card-header">
      <h3 class="doc-name">{{ doc.name }}</h3>
      <GameTag :game="doc.game" size="small" />
    </div>
    <p class="doc-description">{{ doc.description }}</p>
  </div>
</template>

<style scoped>
.doc-card {
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 12px;
  padding: 1.25rem;
  cursor: pointer;
  transition: all 0.2s ease;
}

.doc-card:hover {
  background: rgba(255, 255, 255, 0.06);
  border-color: rgba(255, 255, 255, 0.15);
  transform: translateY(-2px);
}

.doc-card-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
}

.doc-name {
  margin: 0;
  font-size: 1rem;
  font-weight: 600;
  color: #fff;
  word-break: break-word;
}

.doc-description {
  margin: 0;
  font-size: 0.875rem;
  color: #888;
  line-height: 1.5;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
</style>
