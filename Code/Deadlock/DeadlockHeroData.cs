using System;

namespace Deadlock;

/// <summary>
/// A hero's movement numbers, as Deadlock's VData has them: its starting stats in <c>scripts/heroes.vdata</c> and its
/// movement abilities (jump, dash, slide, mantle) in <c>scripts/abilities.vdata</c>. Distances and speeds the game
/// gives in meters are converted to game units here. Every value starts at the game's own, so a hero still moves
/// right if a file is missing.
/// </summary>
public sealed class DeadlockHeroData
{
	/// <summary>
	/// Game units in a meter (an inch per unit): VData's "meters" stats convert with it, and the animation graphs'
	/// own speeds agree (Drifter's 4.75 m/s crouch speed is the 187 units/s its crouch run blend space is built on).
	/// </summary>
	public const float UnitsPerMeter = 39.37f;

	public string Name { get; private init; }
	public string ModelName { get; private init; } = "models/heroes_wip/drifter/drifter.vmdl";

	// Starting stats.
	public float MaxMoveSpeed { get; private init; } = 6.9f * UnitsPerMeter;
	public float SprintSpeed { get; private init; } = 1.6f * UnitsPerMeter;
	public float CrouchSpeed { get; private init; } = 4.75f * UnitsPerMeter;
	public float MoveAcceleration { get; private init; } = 4f;
	public float Stamina { get; private init; } = 3f;
	public float StaminaRegenPerSecond { get; private init; } = 0.222222f;
	public float GroundDashDistance { get; private init; } = 10f * UnitsPerMeter;
	public float GroundDashDuration { get; private init; } = 0.68f;
	public float AirDashDistance { get; private init; } = 8f * UnitsPerMeter;
	public float AirDashDuration { get; private init; } = 0.47f;

	/// <summary>
	/// How far through its distance a ground dash is, by how far through its duration (both 0 to 1).
	/// </summary>
	public DeadlockCurve GroundDashCurve { get; private init; } = DeadlockCurve.Linear;

	// citadel_ability_jump. Defaults the VData leaves out are the jump ability's own (CAbilityJumpVData in server.dll).
	/// <summary>
	/// The upward speed of a ground jump, and of an air jump too: the game's air jump sets it the same way (the
	/// ability's AirJumpVerticalSpeedPercent only applies to the retired double jump item).
	/// </summary>
	public float JumpSpeed { get; private init; } = 300f;

	/// <summary>
	/// The most an air jump adds to the hero's speed along the keys (m_flMaxSpeedDelta).
	/// </summary>
	public float AirJumpMaxSpeedDelta { get; private init; } = 200f;
	public float LateJumpGraceWindow { get; private init; } = 0.15f;
	public float DashJumpStartTime { get; private init; } = 0.3f;
	public float DashJumpEndTime { get; private init; } = 0.55f;
	public float DashJumpSpeed { get; private init; } = 425f;
	public float DashJumpDistance { get; private init; } = 19f * UnitsPerMeter;
	public float DashJumpMissMaxSpeed { get; private init; } = 250f;

	/// <summary>
	/// For how long after a dash jump air control is stronger, and by how many percent.
	/// </summary>
	public float DashJumpExtraAirControlTime { get; private init; } = 0.25f;
	public float DashJumpExtraAirControlPercent { get; private init; } = 30f;
	public float SlideLeapSpeedPenaltyMax { get; private init; } = 100f;
	public float SlideLeapSpeedPenaltyTime { get; private init; } = 0.2f;

	// Wall jumps (citadel_ability_jump): off a wall the hero is in the air next to. Power recharges over the full
	// power recharge time since the last one (from the minimum, along a bias curve) and scales the push.
	public float WallJumpUpSpeed { get; private init; } = 525f;
	public float WallJumpMinOutSpeed { get; private init; } = 225f;
	public float WallJumpMaxOutSpeed { get; private init; } = 310f;
	public float WallJumpMaxLateralSpeed { get; private init; } = 100f;
	/// <summary>
	/// After a wall jump, steering back toward the wall accelerates harder for this long, fading out
	/// (<c>m_flWallJumpLateralInputSuppressTime</c>, which the game describes so despite its name), by up to
	/// <see cref="WallJumpReturnToWallBonusAccel"/> (0 in the game).
	/// </summary>
	public float WallJumpLateralInputSuppressTime { get; private init; } = 0.95f;
	public float WallJumpReturnToWallBonusAccel { get; private init; } = 0f;
	public float WallJumpFullPowerRechargeTime { get; private init; } = 1.25f;
	public float WallJumpPowerMin { get; private init; } = 0.2f;
	public float WallJumpPowerBias { get; private init; } = 0.2f;

	/// <summary>
	/// How far from a wall a wall jump reaches, by speed: from the first distance at the first speed to the second at
	/// the second (<c>m_flRemapSpeedToWallJumpVelocityDist</c>).
	/// </summary>
	public (float MinSpeed, float MaxSpeed, float MinDistance, float MaxDistance) WallJumpReach { get; private init; } = (300f, 600f, 10f, 30f);

	/// <summary>
	/// How much of the lateral (along the wall) push a wall jump gives, by how fast the hero already moves along it.
	/// </summary>
	public DeadlockCurve WallJumpLateralFalloff { get; private init; } = DeadlockCurve.Linear;

	/// <summary>
	/// The stamina regeneration a wall jump takes away (0.25 is a quarter) and for how long.
	/// </summary>
	public float WallJumpStaminaRegenReduction { get; private init; } = 0.25f;
	public float WallJumpStaminaRegenReductionDuration { get; private init; } = 5f;

	// Melee (the hero's ESlot_Weapon_Melee ability, a citadel_hold_melee): a light attack on a short press, a heavy
	// one charged by holding.
	public MeleeAttack LightMelee { get; private init; }
	public MeleeAttack HeavyMelee { get; private init; }
	public MeleeAttack HeavyAirMelee { get; private init; }
	public string MeleeHoldParticle { get; private init; } = "particles/abilities/melee/melee_heavy_activate_charge.vpcf";

	/// <summary>
	/// A melee press released within this long is a light attack; held for the required hold time (from the press),
	/// a heavy one strikes.
	/// </summary>
	public float LightMeleeMaxHoldTime { get; private init; } = 0.2f;
	public float HeavyMeleeRequiredHoldTime { get; private init; } = 0.55f;

	/// <summary>
	/// How fast the hero moves while charging a heavy melee (the ability's ChannelMoveSpeed).
	/// </summary>
	public float MeleeChargeMoveSpeed { get; private init; } = 1.3f * UnitsPerMeter;

	/// <summary>
	/// A melee attack: how fast it lunges over its time (<c>m_MovementSpeedCurve</c>), how much it slows the hero's
	/// running over its time (<c>m_SpeedBonusCurve</c>, negative), its cooldown, how long the attack state lasts and
	/// its particles.
	/// </summary>
	public sealed record MeleeAttack( DeadlockCurve MovementSpeed, DeadlockCurve SpeedBonus, float Cooldown, float StateTime, string SwingParticle, string AttackParticle );

	// The particle systems the movement abilities play.
	public string AirJumpParticle { get; private init; } = "particles/generic/air_jump.vpcf";
	public string DashJumpParticle { get; private init; } = "particles/generic/roll_jump.vpcf";
	public string WallJumpParticle { get; private init; } = "particles/generic/wall_jump.vpcf";
	public string DashParticle { get; private init; } = "particles/generic/air_dash.vpcf";
	public string SlideParticle { get; private init; } = "particles/generic/slide.vpcf";

	// citadel_ability_dash. Defaults the VData leaves out are CAbilityDashVData's own.
	/// <summary>
	/// An air dash's speed cap falls from its full speed to this much of it over the dash.
	/// </summary>
	public float AirDashEndVelocityScale { get; private init; } = 0.2f;

	/// <summary>
	/// How much an air dash changes air acceleration, in percent (-40 is 60% of it).
	/// </summary>
	public float AirDashAccelerationPercent { get; private init; } = 0f;

	/// <summary>
	/// Scales the little upward speed an air dash leaves the hero each tick (m_flDuringDrag).
	/// </summary>
	public float AirDashDuringDrag { get; private init; } = 1f;

	/// <summary>
	/// After an air dash, for the post drag duration, a hero faster than the min drag speed drags, up to the post max
	/// drag at the max drag speed.
	/// </summary>
	public float DashAirSpeedForMinDrag { get; private init; } = 10f * UnitsPerMeter;
	public float DashAirSpeedForMaxDrag { get; private init; } = 14f * UnitsPerMeter;
	public float DashPostMaxDrag { get; private init; } = 4f;
	public float DashPostDragDuration { get; private init; } = 0.1f;
	public float DashSlideWindow { get; private init; } = 0.2f;

	// citadel_ability_slide.
	public float SlideCooldown { get; private init; } = 0.85f;

	/// <summary>
	/// How long a slide lasts at least: the slide ability's duration. Letting go of crouch ends it after that.
	/// </summary>
	public float SlideMinDuration { get; private init; } = 0.6f;
	public float SlideTurnSpeed { get; private init; } = 90f;
	public float SlideTurnMinAngle { get; private init; } = 5f;
	public float SlideMinSustainSpeed { get; private init; } = 170f;
	public float SlideMinActivationSpeed { get; private init; } = 350f;
	public float SlideDashMinActivationSpeed { get; private init; } = 250f;
	public float SlideDashSpeed { get; private init; } = 650f;
	public float SlideDashFailSpeed { get; private init; } = 450f;
	public float SlideDashStartTime { get; private init; } = 0.3f;
	public float SlideDashFriction { get; private init; } = 0.175f;
	public float SlideDashFrictionTime { get; private init; } = 1f;
	public float SlideFrictionFlatGround { get; private init; } = 0.8f;
	public float SlideFrictionFlatGroundGrace { get; private init; } = 0.05f;
	public float SlideFlatGroundFrictionGraceTime { get; private init; } = 0.6f;
	public float SlideFrictionMinSlope { get; private init; } = 0.15f;
	public float SlideFrictionMaxSlope { get; private init; } = 0.15f;
	public float SlideFrictionUphillMinSlope { get; private init; } = 1f;
	public float SlideFrictionUphillMaxSlope { get; private init; } = 1.5f;
	public float SlideMinAngleToConsiderASlope { get; private init; } = 8f;
	public float SlideSlopeFacingAngleToActivate { get; private init; } = 75f;
	public float SlideAccMinSlopeDeg { get; private init; } = 5f;
	public float SlideAccMaxSlopeDeg { get; private init; } = 45f;
	public float SlideAccMinSlopeScale { get; private init; } = 0.5f;
	public float SlideMinSlopeAcceleration { get; private init; } = 500f;
	public float SlideMaxSlopeAcceleration { get; private init; } = 850f;
	public float SlideMinSlopeMaxAccSpeed { get; private init; } = 1200f;
	public float SlideMaxSlopeMaxAccSpeed { get; private init; } = 1500f;
	/// <summary>
	/// The air drag after the second and later slide jumps in a row, fading out over the time. A jump within the late
	/// slide jump window after a slide counts as a slide jump; standing on the ground for the reset time ends the run.
	/// </summary>
	public float SlideAirDragAfterJump { get; private init; } = 2f;
	public float SlideAirDragAfterJumpTime { get; private init; } = 0.5f;
	public float SlideAirDragResetTime { get; private init; } = 0.5f;
	public float LateSlideJumpWindow { get; private init; } = 0.25f;

	// citadel_ability_mantle.
	public float AutoMantlePushTime { get; private init; } = 0.06f;
	public IReadOnlyList<MantleType> MantleTypes { get; private init; } =
	[
		new( 32, 0, 64, 0.2f, 0.2f ),
		new( 64, 32, 96, 0.3f, 0.2f ),
		new( 96, 64, 128, 0.4f, 0.2f ),
		new( 128, 96, 160, 0.5f, 0.2f ),
	];

	/// <summary>
	/// A ledge height band a mantle covers, the animation it plays (<c>mantle_32</c> etc.) and how long it rises and
	/// then moves over the ledge.
	/// </summary>
	public readonly record struct MantleType( int AnimHeight, float MinHeight, float MaxHeight, float VerticalTime, float HorizontalTime );

	/// <summary>
	/// A hero's data, e.g. <c>hero_drifter</c>, or null when the mounted VData has no such hero.
	/// </summary>
	public static DeadlockHeroData Load( string hero )
	{
		if ( DeadlockVData.Find( "scripts/heroes.vdata" )?.Get( hero ) is not { } heroData )
			return null;

		var abilitiesFile = DeadlockVData.Find( "scripts/abilities.vdata" );
		var stats = heroData.GetSubCollection( "m_mapStartingStats" ) ?? KVObject.Collection();
		var abilities = heroData.GetSubCollection( "m_mapBoundAbilities" ) ?? KVObject.Collection();

		KVObject Ability( string slot, string fallback )
			=> abilitiesFile?.Get( abilities.GetStringProperty( slot ) ?? fallback ) ?? KVObject.Collection();

		var jump = Ability( "ESlot_Ability_Jump", "citadel_ability_jump" );
		var slide = Ability( "ESlot_Ability_Slide", "citadel_ability_slide" );
		var dash = Ability( "ESlot_Ability_Innate_1", "citadel_ability_dash" );
		var mantle = Ability( "ESlot_Ability_Mantle", "citadel_ability_mantle" );
		var melee = Ability( "ESlot_Weapon_Melee", "citadel_hold_melee" );
		var attacks = melee.GetSubCollection( "m_mapAttacks" );

		var defaults = new DeadlockHeroData();

		return new DeadlockHeroData
		{
			Name = hero,
			ModelName = heroData.GetStringProperty( "m_strModelName" ) ?? defaults.ModelName,

			MaxMoveSpeed = stats.GetFloatProperty( "EMaxMoveSpeed", 6.9f ) * UnitsPerMeter,
			SprintSpeed = stats.GetFloatProperty( "ESprintSpeed", 1.6f ) * UnitsPerMeter,
			CrouchSpeed = stats.GetFloatProperty( "ECrouchSpeed", 4.75f ) * UnitsPerMeter,
			MoveAcceleration = stats.GetFloatProperty( "EMoveAcceleration", defaults.MoveAcceleration ),
			Stamina = stats.GetFloatProperty( "EStamina", defaults.Stamina ),
			StaminaRegenPerSecond = stats.GetFloatProperty( "EStaminaRegenPerSecond", defaults.StaminaRegenPerSecond ),
			GroundDashDistance = stats.GetFloatProperty( "EGroundDashDistanceInMeters", 10f ) * UnitsPerMeter,
			GroundDashDuration = stats.GetFloatProperty( "EGroundDashDuration", defaults.GroundDashDuration ),
			AirDashDistance = stats.GetFloatProperty( "EAirDashDistanceInMeters", 8f ) * UnitsPerMeter,
			AirDashDuration = stats.GetFloatProperty( "EAirDashDuration", defaults.AirDashDuration ),
			GroundDashCurve = DeadlockCurve.FromKeyValues( heroData.GetSubCollection( "m_groundDashPositionCurve" ) ) ?? DeadlockCurve.Linear,

			JumpSpeed = AbilityProperty( jump, "VerticalSpeed", defaults.JumpSpeed ),
			AirJumpMaxSpeedDelta = jump.GetFloatProperty( "m_flMaxSpeedDelta", defaults.AirJumpMaxSpeedDelta ),
			DashJumpExtraAirControlTime = jump.GetFloatProperty( "m_flDashJumpExtraAirControlTime", defaults.DashJumpExtraAirControlTime ),
			DashJumpExtraAirControlPercent = jump.GetFloatProperty( "m_flDashJumpExtraAirControlPercent", defaults.DashJumpExtraAirControlPercent ),
			LateJumpGraceWindow = jump.GetFloatProperty( "m_flLateJumpGraceWindow", defaults.LateJumpGraceWindow ),
			DashJumpStartTime = jump.GetFloatProperty( "m_flDashJumpStartTime", defaults.DashJumpStartTime ),
			DashJumpEndTime = jump.GetFloatProperty( "m_flDashJumpEndTime", defaults.DashJumpEndTime ),
			DashJumpSpeed = jump.GetFloatProperty( "m_flDashJumpVerticalSpeed", defaults.DashJumpSpeed ),
			DashJumpDistance = jump.GetFloatProperty( "m_flDashJumpDistanceInMeters", 19f ) * UnitsPerMeter,
			DashJumpMissMaxSpeed = jump.GetFloatProperty( "m_flDashJumpMissMaxSpeed", defaults.DashJumpMissMaxSpeed ),
			SlideLeapSpeedPenaltyMax = AbilityProperty( jump, "SlideLeapSpeedPenaltyMax", defaults.SlideLeapSpeedPenaltyMax ),
			SlideLeapSpeedPenaltyTime = AbilityProperty( jump, "SlideLeapSpeedPenaltyTime", defaults.SlideLeapSpeedPenaltyTime ),
			WallJumpUpSpeed = jump.GetFloatProperty( "m_flWallJumpUpSpeed", defaults.WallJumpUpSpeed ),
			WallJumpMinOutSpeed = jump.GetFloatProperty( "m_flWallJumpMinOutSpeed", defaults.WallJumpMinOutSpeed ),
			WallJumpMaxOutSpeed = jump.GetFloatProperty( "m_flWallJumpMaxOutSpeed", defaults.WallJumpMaxOutSpeed ),
			WallJumpMaxLateralSpeed = jump.GetFloatProperty( "m_flWallJumpMaxLateralSpeed", defaults.WallJumpMaxLateralSpeed ),
			WallJumpLateralInputSuppressTime = jump.GetFloatProperty( "m_flWallJumpLateralInputSuppressTime", defaults.WallJumpLateralInputSuppressTime ),
			WallJumpReturnToWallBonusAccel = jump.GetFloatProperty( "m_flWallJumpReturnToWallBonusAccel", defaults.WallJumpReturnToWallBonusAccel ),
			WallJumpFullPowerRechargeTime = jump.GetFloatProperty( "m_flWallJumpFullPowerRechargeTime", defaults.WallJumpFullPowerRechargeTime ),
			WallJumpPowerMin = jump.GetFloatProperty( "m_flWallJumpPowerMin", defaults.WallJumpPowerMin ),
			WallJumpPowerBias = jump.GetFloatProperty( "m_flWallJumpPowerBias", defaults.WallJumpPowerBias ),
			WallJumpReach = jump.ContainsKey( "m_flRemapSpeedToWallJumpVelocityDist" ) && jump.GetFloatArray( "m_flRemapSpeedToWallJumpVelocityDist" ) is { Length: 4 } reach
				? (reach[0], reach[1], reach[2], reach[3])
				: defaults.WallJumpReach,
			WallJumpLateralFalloff = DeadlockCurve.FromKeyValues( jump.GetSubCollection( "m_WallJumpLateralSpeedFalloffVsAlongSpeed" ) ) ?? DeadlockCurve.Linear,
			WallJumpStaminaRegenReduction = -AbilityProperty( jump, "StaminaRegenReduction", -25f ) / 100f,
			WallJumpStaminaRegenReductionDuration = AbilityProperty( jump, "StaminaRegenReductionDuration", defaults.WallJumpStaminaRegenReductionDuration ),
			AirJumpParticle = jump.GetStringProperty( "m_AirJumpParticle" ) ?? defaults.AirJumpParticle,
			DashJumpParticle = jump.GetStringProperty( "m_DashJumpParticle" ) ?? defaults.DashJumpParticle,
			WallJumpParticle = jump.GetStringProperty( "m_WallJumpParticle" ) ?? defaults.WallJumpParticle,
			DashParticle = dash.GetStringProperty( "m_DashParticle" ) ?? defaults.DashParticle,
			SlideParticle = slide.GetStringProperty( "m_SlideParticle" ) ?? defaults.SlideParticle,

			LightMelee = ReadMeleeAttack( attacks?.GetSubCollection( "EAttackType_Light" ) ),
			HeavyMelee = ReadMeleeAttack( attacks?.GetSubCollection( "EAttackType_Heavy" ) ),
			HeavyAirMelee = ReadMeleeAttack( attacks?.GetSubCollection( "EAttackType_HeavyAir" ) ?? attacks?.GetSubCollection( "EAttackType_Heavy" ) ),
			MeleeHoldParticle = melee.GetStringProperty( "m_HoldBeginEffect" ) ?? defaults.MeleeHoldParticle,
			LightMeleeMaxHoldTime = melee.GetFloatProperty( "m_flLightAttackMaxHoldTime", defaults.LightMeleeMaxHoldTime ),
			HeavyMeleeRequiredHoldTime = melee.GetFloatProperty( "m_flHeavyAttackRequiredHoldTime", defaults.HeavyMeleeRequiredHoldTime ),
			MeleeChargeMoveSpeed = AbilityProperty( melee, "ChannelMoveSpeed", defaults.MeleeChargeMoveSpeed ),

			AirDashEndVelocityScale = dash.GetFloatProperty( "m_flAirDashEndVelocityScale", defaults.AirDashEndVelocityScale ),
			AirDashAccelerationPercent = dash.GetFloatProperty( "m_flAirDashAccPct", defaults.AirDashAccelerationPercent ),
			AirDashDuringDrag = dash.GetFloatProperty( "m_flDuringDrag", defaults.AirDashDuringDrag ),
			DashAirSpeedForMinDrag = dash.GetFloatProperty( "m_flAirSpeedForMinDrag", 10f ) * UnitsPerMeter,
			DashAirSpeedForMaxDrag = dash.GetFloatProperty( "m_flAirSpeedForMaxDrag", 14f ) * UnitsPerMeter,

			// The VData's m_flPostDrag is an old name the game no longer reads; m_flPostMaxDrag is the field.
			DashPostMaxDrag = dash.GetFloatProperty( "m_flPostMaxDrag", defaults.DashPostMaxDrag ),
			DashPostDragDuration = dash.GetFloatProperty( "m_flPostDragDuration", defaults.DashPostDragDuration ),
			DashSlideWindow = dash.GetFloatProperty( "m_flSlideEarlyOutWindow", defaults.DashSlideWindow ),

			SlideCooldown = AbilityProperty( slide, "AbilityCooldown", defaults.SlideCooldown ),
			SlideMinDuration = AbilityProperty( slide, "AbilityDuration", defaults.SlideMinDuration ),
			SlideTurnSpeed = slide.GetFloatProperty( "m_flTurnSpeed", defaults.SlideTurnSpeed ),
			SlideTurnMinAngle = slide.GetFloatProperty( "m_flTurnMinAngDiff", defaults.SlideTurnMinAngle ),
			SlideMinSustainSpeed = slide.GetFloatProperty( "m_flMinSustainSpeed", defaults.SlideMinSustainSpeed ),
			SlideMinActivationSpeed = slide.GetFloatProperty( "m_flMinActivationSpeed", defaults.SlideMinActivationSpeed ),
			SlideDashMinActivationSpeed = slide.GetFloatProperty( "m_flDashMinActivationSpeed", defaults.SlideDashMinActivationSpeed ),
			SlideDashSpeed = slide.GetFloatProperty( "m_flDashSlideSpeed", defaults.SlideDashSpeed ),
			SlideDashFailSpeed = slide.GetFloatProperty( "m_flDashSlideFailSpeed", defaults.SlideDashFailSpeed ),
			SlideDashStartTime = slide.GetFloatProperty( "m_flDashSlideStartTime", defaults.SlideDashStartTime ),
			SlideDashFriction = slide.GetFloatProperty( "m_flDashSlideFriction", defaults.SlideDashFriction ),
			SlideDashFrictionTime = slide.GetFloatProperty( "m_flDashSlideFrictionTime", defaults.SlideDashFrictionTime ),
			SlideFrictionFlatGround = slide.GetFloatProperty( "m_flFrictionFlatGround", defaults.SlideFrictionFlatGround ),
			SlideFrictionFlatGroundGrace = slide.GetFloatProperty( "m_flFrictionFlatGroundGrace", defaults.SlideFrictionFlatGroundGrace ),
			SlideFlatGroundFrictionGraceTime = slide.GetFloatProperty( "m_flFlatGroundFrictionGraceTime", defaults.SlideFlatGroundFrictionGraceTime ),
			SlideFrictionMinSlope = slide.GetFloatProperty( "m_flFrictionMinSlope", defaults.SlideFrictionMinSlope ),
			SlideFrictionMaxSlope = slide.GetFloatProperty( "m_flFrictionMaxSlope", defaults.SlideFrictionMaxSlope ),
			SlideFrictionUphillMinSlope = slide.GetFloatProperty( "m_flFrictionUphillMinSlope", defaults.SlideFrictionUphillMinSlope ),
			SlideFrictionUphillMaxSlope = slide.GetFloatProperty( "m_flFrictionUphillMaxSlope", defaults.SlideFrictionUphillMaxSlope ),
			SlideMinAngleToConsiderASlope = slide.GetFloatProperty( "m_flMinAngleToConsiderASlope", defaults.SlideMinAngleToConsiderASlope ),
			SlideSlopeFacingAngleToActivate = slide.GetFloatProperty( "m_flSlopeFacingAngleToActivate", defaults.SlideSlopeFacingAngleToActivate ),
			SlideAccMinSlopeDeg = slide.GetFloatProperty( "m_flAccMinSlopeDeg", defaults.SlideAccMinSlopeDeg ),
			SlideAccMaxSlopeDeg = slide.GetFloatProperty( "m_flAccMaxSlopeDeg", defaults.SlideAccMaxSlopeDeg ),
			SlideAccMinSlopeScale = slide.GetFloatProperty( "m_flAccMinSlopeScale", defaults.SlideAccMinSlopeScale ),
			SlideMinSlopeAcceleration = slide.GetFloatProperty( "m_flSlideMinSlopeAcceleration", defaults.SlideMinSlopeAcceleration ),
			SlideMaxSlopeAcceleration = slide.GetFloatProperty( "m_flSlideMaxSlopeAcceleration", defaults.SlideMaxSlopeAcceleration ),
			SlideMinSlopeMaxAccSpeed = slide.GetFloatProperty( "m_flSlideMinSlopeMaxAccSpeed", defaults.SlideMinSlopeMaxAccSpeed ),
			SlideMaxSlopeMaxAccSpeed = slide.GetFloatProperty( "m_flSlideMaxSlopeMaxAccSpeed", defaults.SlideMaxSlopeMaxAccSpeed ),
			SlideAirDragAfterJump = slide.GetFloatProperty( "m_flAirDragAfterJump", defaults.SlideAirDragAfterJump ),
			SlideAirDragAfterJumpTime = slide.GetFloatProperty( "m_flAirDragAfterJumpTime", defaults.SlideAirDragAfterJumpTime ),
			SlideAirDragResetTime = slide.GetFloatProperty( "m_flAirDragResetTime", defaults.SlideAirDragResetTime ),
			LateSlideJumpWindow = slide.GetFloatProperty( "m_flLateSlideJumpWindow", defaults.LateSlideJumpWindow ),

			AutoMantlePushTime = mantle.GetFloatProperty( "m_flAutoMantlePushTime", defaults.AutoMantlePushTime ),
			MantleTypes = ReadMantleTypes( mantle ) ?? defaults.MantleTypes,
		};
	}

	/// <summary>
	/// An ability property (<c>m_mapAbilityProperties</c>), which VData keeps as text, sometimes with a unit: "1.3m"
	/// is in meters (per second), converted to game units.
	/// </summary>
	private static float AbilityProperty( KVObject ability, string name, float fallback )
	{
		if ( ability.GetSubCollection( "m_mapAbilityProperties" )?.GetSubCollection( name ) is not { } property || property["m_strValue"] is not { } value )
			return fallback;

		var text = value.ToString().Trim();
		var scale = 1f;
		if ( text.EndsWith( 'm' ) )
		{
			text = text[..^1];
			scale = UnitsPerMeter;
		}

		return float.TryParse( text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number ) ? number * scale : fallback;
	}

	private static MeleeAttack ReadMeleeAttack( KVObject attack )
	{
		if ( attack is null )
			return null;

		return new MeleeAttack(
			DeadlockCurve.FromKeyValues( attack.GetSubCollection( "m_MovementSpeedCurve" ) ),
			DeadlockCurve.FromKeyValues( attack.GetSubCollection( "m_SpeedBonusCurve" ) ),
			attack.GetFloatProperty( "m_flCooldownOnMiss", 0.75f ),
			attack.GetFloatProperty( "m_flAttackStateTime", 0.5f ),
			attack.GetStringProperty( "m_MeleeSwingParticle" ),
			attack.GetStringProperty( "m_MeleeAttackParticle" ) );
	}

	private static IReadOnlyList<MantleType> ReadMantleTypes( KVObject mantle )
	{
		if ( !mantle.ContainsKey( "m_vecMantleTypes" ) || mantle.GetArray( "m_vecMantleTypes" ) is not { Count: > 0 } types )
			return null;

		// Times the game leaves out are the defaults its mantle animations are made for.
		return types.Select( x => new MantleType(
			(int)x.GetFloatProperty( "m_flAnimHeight", 64 ),
			x.GetFloatProperty( "m_flMinHeight", 0 ),
			x.GetFloatProperty( "m_flMaxHeight", 64 ),
			x.GetFloatProperty( "m_flVerticalTime", 0.2f ),
			x.GetFloatProperty( "m_flHorizontalTime", 0.2f ) ) ).ToList();
	}
}

/// <summary>
/// A Deadlock curve (<c>m_spline</c> keys with in and out slopes), evaluated as a Hermite spline between its keys.
/// </summary>
public sealed class DeadlockCurve
{
	public static DeadlockCurve Linear { get; } = new( [new( 0, 0, 1, 1 ), new( 1, 1, 1, 1 )] );

	private readonly Key[] keys;

	private readonly record struct Key( float X, float Y, float SlopeIn, float SlopeOut );

	private DeadlockCurve( Key[] keys ) => this.keys = keys;

	/// <summary>
	/// Where the curve's last key is: past it, the curve holds its last value.
	/// </summary>
	public float End => keys[^1].X;

	public static DeadlockCurve FromKeyValues( KVObject curve )
	{
		if ( curve is null || !curve.ContainsKey( "m_spline" ) || curve.GetArray( "m_spline" ) is not { Count: > 0 } spline )
			return null;

		return new DeadlockCurve( spline
			.Select( x => new Key( x.GetFloatProperty( "x" ), x.GetFloatProperty( "y" ), x.GetFloatProperty( "m_flSlopeIncoming" ), x.GetFloatProperty( "m_flSlopeOutgoing" ) ) )
			.OrderBy( x => x.X )
			.ToArray() );
	}

	public float Evaluate( float x )
	{
		if ( x <= keys[0].X )
			return keys[0].Y;

		if ( x >= keys[^1].X )
			return keys[^1].Y;

		var i = 0;
		while ( x > keys[i + 1].X )
			i++;

		var a = keys[i];
		var b = keys[i + 1];
		var width = b.X - a.X;
		if ( width <= 0f )
			return b.Y;

		var t = (x - a.X) / width;
		var t2 = t * t;
		var t3 = t2 * t;

		return (2 * t3 - 3 * t2 + 1) * a.Y + (t3 - 2 * t2 + t) * width * a.SlopeOut + (-2 * t3 + 3 * t2) * b.Y + (t3 - t2) * width * b.SlopeIn;
	}
}
