# Guide de contribution — NinaTheSkyX

## Structure du projet

```
NinaTheSkyX/        ← code source du plugin (ce dépôt)
NinaTheSkyX.Tests/  ← tests xUnit (dossier sibling, même répertoire parent)
```

## Pré-requis

- Windows x64, .NET 8 SDK
- NINA 3.x pour les tests manuels (optionnel pour le build et les tests unitaires)
- TheSkyX 64 pour les tests sur ciel réel

## Workflow git

Push direct sur `main`. Pas de branches obligatoires.

```bash
# Avant de commencer à travailler
git pull

# Après vos modifications
git add .
git commit -m "feat: description courte"
git push
```

## Conventions de commit

| Préfixe | Usage |
|---|---|
| `feat:` | Nouvelle fonctionnalité |
| `fix:` | Correction de bug |
| `test:` | Ajout ou modification de tests |
| `refactor:` | Refactoring sans changement de comportement |
| `docs:` | Documentation uniquement |
| `chore:` | Maintenance (NuGet, scripts, etc.) |

## Invariants à ne jamais toucher

- **GUID plugin** : `c3d4e5f6-a7b8-9012-cdef-012345678912` (dans `AssemblyInfo.cs` et `PluginOptions.cs`)
- **Nom de l'assembly** : `NinaTheSkyX`
- **x:Key du DataTemplate XAML** : `"TheSkyX Guider_Options"` (espace obligatoire)

Voir [`CLAUDE.md`](CLAUDE.md) pour l'ensemble des conventions et pièges connus.

## Avant de pousser

```powershell
cd ..\NinaTheSkyX.Tests
dotnet test -c Release --nologo
# Cible : 65/65 verts
```

## Pour les sessions avec IA (Claude)

1. **`git pull`** en début de session — intégrer les commits des autres devs
2. Claude lit `CLAUDE.md` et `TECHNICAL_STATE.md` pour se mettre en contexte
3. En fin de session : `git commit` descriptif + `git push`

Les fichiers [`CLAUDE.md`](CLAUDE.md) et [`TECHNICAL_STATE.md`](TECHNICAL_STATE.md) sont la mémoire du projet — les tenir à jour.
