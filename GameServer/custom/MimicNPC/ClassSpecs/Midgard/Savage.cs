using DOL.GS.PlayerClass;

namespace DOL.GS.Scripts
{
    //public class MimicSavage : MimicNPC
    //{
    //    public MimicSavage(byte level) : base(new ClassSavage(), level)
    //    { }
    //}

    public class SavageSpec : MimicSpec
    {
        public SavageSpec(eSpecType spec)
        {
            SpecName = "SavageSpec";

            var randBaseWeap = spec switch
            {
                eSpecType.TwoHanded => Util.Random(0, 2),
                eSpecType.Mid => Util.Random(0, 2),
                eSpecType.DualWield => Util.Random(3, 4),
                _ => Util.Random(4),
            };

            switch (randBaseWeap)
            {
                case 0: WeaponOneType = eObjectType.Sword; break;
                case 1: WeaponOneType = eObjectType.Axe; break;
                case 2: WeaponOneType = eObjectType.Hammer; break;
                case 3:
                case 4: WeaponOneType = eObjectType.HandToHand; break;
            }

            if (WeaponOneType != eObjectType.HandToHand)
            {
                Is2H = true;
                SpecType = eSpecType.Mid;
            }
            else
                SpecType = eSpecType.DualWield;

            int randVariance = Util.Random(3);

            switch (randVariance)
            {
                case 0:
                Add(ObjToSpec(WeaponOneType), 44, 0.7f);
                Add(Specs.Savagery, 49, 0.9f);
                Add(Specs.Parry, 4, 0.0f);
                break;

                case 1:
                Add(ObjToSpec(WeaponOneType), 39, 0.7f);
                Add(Specs.Savagery, 49, 0.9f);
                Add(Specs.Parry, 20, 0.1f);
                break;

                case 2:
                Add(ObjToSpec(WeaponOneType), 44, 0.7f);
                Add(Specs.Savagery, 48, 0.9f);
                Add(Specs.Parry, 10, 0.1f);
                break;

                case 3:
                Add(ObjToSpec(WeaponOneType), 50, 0.7f);
                Add(Specs.Savagery, 42, 0.9f);
                Add(Specs.Parry, 9, 0.1f);
                break;
            }
        }
    }
}