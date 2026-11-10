# Biblioteca comunitaria

Catálogo público de perfiles y efectos para DualSense Ultimate. La aplicación descarga `index.json`
(Biblioteca comunitaria → Actualizar catálogo) y cada paquete de `packages/`.

## Publicar

1. En la aplicación, exporta tu perfil, efecto de gatillo, patrón háptico, preset de iluminación,
   macro o configuración de giroscopio (botón **Exportar…** de su sección).
2. Copia el archivo a `community/packages/` con un nombre en minúsculas y guiones.
3. Añade una entrada en `community/index.json` (`id`, `name`, `kind`, `author`, `description`,
   `game`, `version`, `url`).
4. Abre un pull request. La CI importa todos los paquetes con el validador de la aplicación
   (`CommunityCatalogTests`); un paquete que no pase la validación no se puede fusionar.

Para actualizar un paquete, modifica el archivo y sube `version`.

## Normas

- Solo datos: los paquetes no contienen ni pueden contener código.
- Sin datos personales (el exportador ya elimina el identificador del mando).
- Las valoraciones y comentarios se hacen en el propio pull request o en las discusiones de GitHub.
