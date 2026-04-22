<script setup>
import GameTag from "./GameTag.vue";
import Card from "primevue/card";

const props = defineProps({
  doc: {
    type: Object,
    required: true,
  },
});

const emit = defineEmits(["click"]);
</script>

<template>
  <Card
    class="bg-white/5 border border-white/10 rounded-xl cursor-pointer transition-all hover:bg-white/10 hover:border-white/20 hover:-translate-y-0.5"
    @click="emit('click', doc)"
  >
    <template #content>
      <div class="flex justify-between items-start gap-3 mb-3">
        <h3 class="m-0 text-base font-semibold text-white break-words">
          {{ doc.name }}
        </h3>
        <GameTag :game="doc.game" size="small" />
      </div>
      <p class="m-0 text-sm text-zinc-400 leading-relaxed line-clamp-2">
        {{ doc.description }}
      </p>
    </template>
  </Card>
</template>
