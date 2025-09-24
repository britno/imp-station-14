using System.Linq;
using Content.Client.DamageState; // imp
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.AnimalHusbandry;
using Content.Shared.Sprite;
using Robust.Client.GameObjects;

namespace Content.Client.Nutrition.EntitySystems;

/// <summary>
/// This handles visuals for <see cref="InfantComponent"/>
/// </summary>
public sealed class InfantSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!; // imp
    [Dependency] private readonly MobStateSystem _mobState = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<InfantComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<InfantComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<InfantComponent, AppearanceChangeEvent>(OnAppearanceChange, after: [typeof(VisualizerSystem<DamageStateVisualsComponent>), typeof(DamageStateVisualizerSystem)]); // imp. we need to specify that we do this *after* DamageState gets it, so we can overwrite DamageState's changes
        SubscribeLocalEvent<InfantComponent, MobStateChangedEvent>(OnMobStateChanged); // imp
    }

    // imp method
    private void OnStartup(Entity<InfantComponent> ent, ref ComponentStartup args)
    {
        // get the sprite component of the entity
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        // trigger an appearance change. we have to do this because otherwise the networking gets a little fucky on rare occasions
        _appearance.OnChangeData(ent, sprite);
    }

    // imp method
    private void OnShutdown(Entity<InfantComponent> ent, ref ComponentShutdown args)
    {
        // get the sprite component of the entity
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        // let the component know that the entity is no longer an infant.
        ent.Comp.IsInfant = false;

        // trigger an appearance change. we have to do this because otherwise the networking gets a little fucky on rare occasions
        _appearance.OnChangeData(ent, sprite);
    }

    // imp method. force an appearance change on mob state change
    private void OnMobStateChanged(Entity<InfantComponent> ent, ref MobStateChangedEvent args)
    {
        // get the sprite component of the entity
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;
        _appearance.OnChangeData(ent, sprite);
    }

    // imp method
    private void OnAppearanceChange(Entity<InfantComponent> ent, ref AppearanceChangeEvent args)
    {
        // responding to the appearance change event we raise in OnStartup and OnShutdown, change the sprite to fit whether or not it's an infant.
        ChangeSprite(ent, args.Sprite, args.Component, ent.Comp.IsInfant);
    }

    // imp method. does a bunch of layers fuckery to set infant layers visible and base layers invisible, or vice versa.
    // if there are no such infant layers, falls back to the upstream infant system's solution
    private void ChangeSprite(Entity<InfantComponent> ent, SpriteComponent? sprite, AppearanceComponent appearance, bool setInfant)
    {
        // compiler gets mad if we don't nullcheck, even though `sprite` can't be null here
        if (sprite == null)
            return;

        Color? selectedRandomColor = null;
        if (TryComp<RandomSpriteComponent>(ent, out var randomSprite))
        {
            // deconstruct the selected color KVP, all we need is the value
            var (_, value) = randomSprite.Selected.First().Value;
            selectedRandomColor = value;
        }

        // condense this to an Entity<T> because we're gonna be using it a lot later & assigning it every time is messy
        Entity<SpriteComponent?> entSprite = (ent, sprite);

        // if the sprite has both a layer with the InfantVisuals.Infant map and a layer with the DamageStateVisualLayrs.Base map,
        if (_sprite.TryGetLayer(entSprite, InfantVisuals.Infant, out _, false) &&
            _sprite.TryGetLayer(entSprite, DamageStateVisualLayers.Base, out _, false))
        {
            var isDead = _mobState.IsIncapacitated(ent);

            // set the infant layers' visibility, or dead layer depending on IsDead. if there's no incapacitated layer, don't bother
            if (!isDead || !_sprite.TryGetLayer(entSprite, InfantVisuals.InfantIncapacitated, out _, false))
            {
                foreach (var infantKey in new[] { InfantVisuals.Infant, InfantVisuals.InfantUnshaded })
                {
                    if (!_sprite.TryGetLayer(entSprite, infantKey, out var infantVisualsLayer, false))
                        continue;
                    _sprite.LayerSetVisible(infantVisualsLayer, setInfant);
                    // if there's a random color from RandomSprite, set the layer to that color
                    if (selectedRandomColor is { } selectedColor)
                        _sprite.LayerSetColor(infantVisualsLayer, selectedColor);
                }
                // set the damage state base and unshaded layers' visibility to the opposite of setInfant
                foreach (var dsKey in new[] { DamageStateVisualLayers.Base, DamageStateVisualLayers.BaseUnshaded })
                {
                    if (!_sprite.TryGetLayer(entSprite, dsKey, out var dsLayer, false) || isDead)
                        continue;
                    _sprite.LayerSetVisible(dsLayer, !setInfant);
                }
                // set the dead sprites invisible
                foreach (var deadKey in new[] { InfantVisuals.InfantIncapacitated, InfantVisuals.InfantIncapacitatedUnshaded })
                {
                    if (!_sprite.TryGetLayer(entSprite, deadKey, out var deadLayer, false))
                        continue;
                    _sprite.LayerSetVisible(deadLayer, false);
                }
            }
            // if dead,
            else
            {
                // get the layers that DamageStateVisuals just set visible, and set them back to invisible
                if (setInfant) // don't do this if the carp has grown up while dead.
                {
                    if (TryComp<AppearanceComponent>(ent, out var appearanceComp) && _appearance.TryGetData<MobState>(ent, MobStateVisuals.State, out var data, appearanceComp) &&
                            TryComp<DamageStateVisualsComponent>(ent, out var damageStateComp) && damageStateComp.States.TryGetValue(data, out var layers))
                    {
                        foreach (var (key, _) in layers)
                        {
                            if (!_sprite.LayerMapTryGet(entSprite, key, out _, false))
                                continue;
                            _sprite.LayerSetVisible(entSprite, key, false);
                        }
                    }
                }

                // set infant layers invisible
                foreach (var infantKey in new[] { InfantVisuals.Infant, InfantVisuals.InfantUnshaded })
                {
                    if (!_sprite.TryGetLayer(entSprite, infantKey, out var infantVisualsLayer, false))
                        continue;
                    _sprite.LayerSetVisible(infantVisualsLayer, false);
                }

                // set dead layers visible (or invisible if the entity has grown up while dead. which is something i apparently have to worry about)
                foreach (var deadKey in new[] { InfantVisuals.InfantIncapacitated, InfantVisuals.InfantIncapacitatedUnshaded })
                {
                    if (!_sprite.TryGetLayer(entSprite, deadKey, out var deadLayer, false))
                        continue;
                    _sprite.LayerSetVisible(deadLayer, setInfant);
                    // if there's a random color from RandomSprite, set the layer to that color
                    if (selectedRandomColor is { } selectedColor)
                        _sprite.LayerSetColor(deadLayer, selectedColor);
                }
            }
        }
        // if the sprite doesn't have InfantVisuals.Infant, or if it doesn't have DamageStateVisualLayers.Base,
        // we just do the default logic from upstream.
        else
        {
            ent.Comp.DefaultScale = sprite.Scale;
            _sprite.SetScale((ent, sprite), ent.Comp.VisualScale);
        }
    }
}
