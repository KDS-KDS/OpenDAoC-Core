using DOL.GS.PlayerClass;

namespace DOL.GS.Scripts
{
    //public class MimicMercenary : MimicNPC
    //{
    //    public MimicMercenary(byte level) : base(new ClassMercenary(), level)
    //    { }
    //}

    public class MercenarySpec : MimicSpec
    {
        public MercenarySpec(eSpecType spec)
        {
            SpecName = "MercenarySpec";

            int randBaseWeap = Util.Random(2);

            switch (randBaseWeap)
            {
                case 0: WeaponOneType = eObjectType.SlashingWeapon; break;
                case 1: WeaponOneType = eObjectType.ThrustWeapon; break;
                case 2: WeaponOneType = eObjectType.CrushingWeapon; break;
            }

            var randVariance = spec switch
            {
                eSpecType.DualWield => Util.Random(0, 2),
                eSpecType.DualWieldAndShield => 3,
                _ => Util.Random(3),
            };

            switch (randVariance)
            {
                case 0:
                case 1:
                case 2:
                SpecType = eSpecType.DualWield;
                Add(ObjToSpec(WeaponOneType), 50, 0.8f);
                Add(Specs.Dual_Wield, 50, 1.0f);
                Add(Specs.Parry, 28, 0.2f);
                break;

                case 3:
                SpecType = eSpecType.DualWieldAndShield;
                Add(ObjToSpec(WeaponOneType), 39, 0.8f);
                Add(Specs.Dual_Wield, 50, 0.9f);
                Add(Specs.Shields, 42, 0.5f);
                Add(Specs.Parry, 6, 0.1f);
                break;
            }
        }
    }
}